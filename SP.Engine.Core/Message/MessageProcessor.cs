using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
                EstimatedRtt = sendTimeOutMs / 2.0;
            }

            public IMessage Message { get; }
            public DateTime ExpireTime { get; set; }
            public int LimitReSendCnt { get; }
            public int TimeOutMs { get; set; }
            public int ReSendCnt { get; private set; }
            public long LastSentTime { get; private set; } = Stopwatch.GetTimestamp();
            public double EstimatedRtt { get; set; }
            
            public void IncrementReSendCnt()
            {
                ReSendCnt++;
                LastSentTime = Stopwatch.GetTimestamp();
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
        private readonly ConcurrentDictionary<long, SendingMessageState> _messageStateDict = new ConcurrentDictionary<long, SendingMessageState>();
        private readonly ConcurrentQueue<IMessage> _pendingMessageQueue = new ConcurrentQueue<IMessage>();
        private const double ALPHA = 0.125;
        private const double RTT_VARIANCE_THRESHOLD = 2.0;
        private const double TIMEOUT_MULTIPLIER = 1.5;
        protected int SendTimeOutMs { get; set; }
        protected int MaxReSendCnt { get; set; }

        private readonly object _lock = new object();
        
        protected void AddSendingMessage(IMessage message)
        {
            lock (_lock)
            {
                var sequenceNumber = Interlocked.Increment(ref _expectedSequenceNumber);
                var state = new SendingMessageState(message, SendTimeOutMs, MaxReSendCnt);

                if (!_messageStateDict.TryAdd(sequenceNumber, state))
                {
                    Interlocked.Decrement(ref _expectedSequenceNumber);
                    return;
                }

                message.SetSequenceNumber(sequenceNumber);
            }            
        }

        protected void RegisterPendingMessage(IMessage message)
        {
            _pendingMessageQueue.Enqueue(message);
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
            if (!_messageStateDict.TryRemove(sequenceNumber, out var state)) 
                return;

            var currentTimestamp = Stopwatch.GetTimestamp();
            var elapsedTicks = currentTimestamp - state.LastSentTime;
            if (elapsedTicks <= 0)
                return;
            
            var rtt = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            rtt = Math.Clamp(rtt, state.EstimatedRtt / RTT_VARIANCE_THRESHOLD, state.EstimatedRtt * RTT_VARIANCE_THRESHOLD);

            state.EstimatedRtt = (1 - ALPHA) * state.EstimatedRtt + ALPHA * rtt;
            state.TimeOutMs = (int)(state.EstimatedRtt * TIMEOUT_MULTIPLIER);
        }

        protected IEnumerable<IMessage> CheckMessageTimeout(out bool isLimitExceededReSend)
        {
            isLimitExceededReSend = false;
            var expiredMessages = _messageStateDict.Values
                .Where(state => DateTime.UtcNow >= state.ExpireTime)
                .ToList();

            var result = new List<IMessage>();
            foreach (var state in expiredMessages)
            {
                if (state.ReSendCnt < state.LimitReSendCnt)
                {
                    state.IncrementReSendCnt();
                    result.Add(state.Message);
                }
                else
                {
                    isLimitExceededReSend = true;
                    result.Clear();
                    return result;
                }
            }
            return result;
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
            foreach (var state in _messageStateDict.Values)
                state.Reset();                
        }

        protected void ResetMessageProcessor()
        {
            _expectedSequenceNumber = 0;
            _messageStateDict.Clear();
            _pendingReceiveMessageDict.Clear();
        }
    }
}
