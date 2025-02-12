using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SP.Engine.Core.Message
{
    public abstract class MessageProcessor
    {
        private sealed class SendingMessageState
        {
            public SendingMessageState(IMessage message, int sendTimeOutMs, int limitReSendCnt)
            {
                Message = message;
                ExpireTime = DateTime.UtcNow.AddMilliseconds(sendTimeOutMs);
                LimitReSendCnt = limitReSendCnt;
                TimeOutMs = sendTimeOutMs;
            }

            public IMessage Message { get; }
            public DateTime ExpireTime { get; set; }
            public int LimitReSendCnt { get; }
            public int TimeOutMs { get; set; }
            public int ReSendCnt { get; private set; }
            public long LastSentTime { get; } = Stopwatch.GetTimestamp();
            public double EstimatedRtt { get; set; }
            public double DevRtt { get; set; }
            
            public void IncrementReSendCnt()
            {
                ReSendCnt++;
                ExpireTime = DateTime.UtcNow.AddMilliseconds(TimeOutMs);
            }

            public void Reset()
            {
                ReSendCnt = 0;
                ExpireTime = DateTime.UtcNow;
            }
        }

        private readonly ConcurrentDictionary<long, IMessage> _pendingReceiveMessageDict = new ConcurrentDictionary<long, IMessage>();
        private long _expectedSequenceNumber;
        private readonly ConcurrentDictionary<long, SendingMessageState> _sendingMessageStateDict = new ConcurrentDictionary<long, SendingMessageState>();
        private readonly ConcurrentQueue<IMessage> _pendingMessageQueue = new ConcurrentQueue<IMessage>();
        private const double Alpha = 0.125;
        private const double Beta = 0.25;
        protected int SendTimeOutMs { get; set; }
        protected int MaxReSendCnt { get; set; }
        
        protected void AddSendingMessage(IMessage tcpMessage)
        {
            var sequenceNumber = Interlocked.Increment(ref _expectedSequenceNumber);
            var state = new SendingMessageState(tcpMessage, SendTimeOutMs, MaxReSendCnt);
            if (!_sendingMessageStateDict.TryAdd(sequenceNumber, state)) 
                return;
            
            tcpMessage.SetSequenceNumber(sequenceNumber);
        }

        protected void RegisterPendingMessage(IMessage tcpMessage)
        {
            _pendingMessageQueue.Enqueue(tcpMessage);
        }

        protected IEnumerable<IMessage> GetPendingMessages()
        {
            var result = new List<IMessage>();
            while (_pendingMessageQueue.TryDequeue(out var message))
                result.Add(message);
            return result;
        }

        protected void ReceiveMessageAck(long sequenceNumber)
        {
            if (!_sendingMessageStateDict.TryRemove(sequenceNumber, out var state)) 
                return;
            
            var rtt = (Stopwatch.GetTimestamp() - state.LastSentTime) * 1000.0 / Stopwatch.Frequency;
            UpdateTimeOut(state, rtt);
        }

        protected IEnumerable<IMessage> FindExpiredMessages(out bool isLimitExceededReSend)
        {
            isLimitExceededReSend = false;

            var result = new List<IMessage>();
            foreach (var kv in _sendingMessageStateDict)
            {
                var state = kv.Value;
                if (DateTime.UtcNow < state.ExpireTime)
                    continue;
                
                if (state.ReSendCnt >= state.LimitReSendCnt)
                {
                    isLimitExceededReSend = true;
                    result.Clear();
                    return result;
                }
                
                state.IncrementReSendCnt();
                result.Add(state.Message);
            }
            return result;
        }

        private void UpdateTimeOut(SendingMessageState state, double rttMs)
        {
            if (state.EstimatedRtt < 1e-10)
            {
                state.EstimatedRtt = rttMs;
                state.DevRtt = rttMs / 2.0;
            }
            else
            {
                state.EstimatedRtt = (1 - Alpha) * state.EstimatedRtt + Alpha * rttMs;
                state.DevRtt = (1 - Beta) * state.DevRtt + Beta * Math.Abs(rttMs - state.EstimatedRtt);
            }

            state.TimeOutMs = (int)(state.EstimatedRtt + 4 * state.DevRtt);
            if (state.TimeOutMs < SendTimeOutMs) 
                state.TimeOutMs = SendTimeOutMs;

            state.ExpireTime = DateTime.UtcNow.AddMilliseconds(state.TimeOutMs);
        }

        protected IEnumerable<IMessage> GetPendingReceivedMessages(IMessage message)
        {
            var result = new List<IMessage>();
            if (message.SequenceNumber <= _expectedSequenceNumber || !_pendingReceiveMessageDict.TryAdd(message.SequenceNumber, message))
            {
                // 이미 처리했거나 대기 중인 메시지임
                return result;
            }
            
            while (_pendingReceiveMessageDict.TryRemove(_expectedSequenceNumber + 1, out var pending))
            {
                // 순서대로 메시지 처리함
                result.Add(pending);
                _expectedSequenceNumber++;
            }
            
            return result;
        }

        protected void ResetSendingMessageStates()
        {
            foreach (var state in _sendingMessageStateDict.Values)
                state.Reset();                
        }

        protected void ResetMessageProcessor()
        {
            _expectedSequenceNumber = 0;
            _sendingMessageStateDict.Clear();
            _pendingReceiveMessageDict.Clear();
        }
    }
}
