using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SP.Common;

namespace SP.Engine.Runtime.Message
{
    public abstract class MessageProcessor
    {
        private sealed class SendingMessageState
        {
            private const double TimeoutScaleFactor = 1.5;

            public SendingMessageState(IMessage message, int sendTimeOutMs, int maxReSendCnt)
            {
                Message = message;
                MaxReSendCnt = maxReSendCnt;
                TimeOutMs = sendTimeOutMs;
                LastSendTime = Stopwatch.GetTimestamp();
                ExpireTime = DateTime.UtcNow.AddMilliseconds(sendTimeOutMs);
            }
            
            public IMessage Message { get; }
            public int MaxReSendCnt { get; }
            public int ReSendCnt { get; private set; }
            public int TimeOutMs { get; private set; }
            public long LastSendTime { get; private set; }
            public DateTime ExpireTime { get; private set; }

            public bool HasExpired => DateTime.UtcNow >= ExpireTime;
            
            public void IncrementReSendCnt()
            {
                ReSendCnt++;
                LastSendTime = Stopwatch.GetTimestamp();
                ExpireTime = DateTime.UtcNow.AddMilliseconds(TimeOutMs);
            }

            public void UpdateTimeout(double estimatedRtt)
                => TimeOutMs = (int)(estimatedRtt * TimeoutScaleFactor);
            
            public void Reset()
            {
                ReSendCnt = 0;
                ExpireTime = DateTime.UtcNow;
            }
        }

        private const int InitialRttMs = 100;
        private const double Alpha = 0.125;
        private const double RttClampRangeFactor = 2.0;
        
        private readonly ConcurrentDictionary<long, IMessage> _pendingReceiveMessages = new ConcurrentDictionary<long, IMessage>();
        private long _nextSendSequenceNumber;
        private long _expectedReceiveSequenceNumber;
        private readonly ConcurrentDictionary<long, SendingMessageState> _sendingMessageStates = new ConcurrentDictionary<long, SendingMessageState>();
        private readonly ConcurrentQueue<IMessage> _pendingMessages = new ConcurrentQueue<IMessage>();
        private readonly EwmaTracker _rttTracker;
        
        protected int SendTimeOutMs { get; private set; }
        protected int MaxReSendCnt { get; private set; }

        private readonly object _lock = new object();
        
        public void SetSendTimeOutMs(int sendTimeOutMs) => SendTimeOutMs = sendTimeOutMs;
        public void SetMaxReSendCnt(int maxReSendCnt) => MaxReSendCnt = maxReSendCnt;

        protected MessageProcessor()
        {
            _rttTracker = new EwmaTracker(Alpha);
            _rttTracker.Reset(InitialRttMs);
        }
        
        protected void RegisterSendingMessage(IMessage message)
        {
            if (message.SequenceNumber != 0)
                return;
            
            lock (_lock)
            {
                var sequenceNumber = Interlocked.Increment(ref _nextSendSequenceNumber);
                var state = new SendingMessageState(message, SendTimeOutMs, MaxReSendCnt);

                if (_sendingMessageStates.TryAdd(sequenceNumber, state))
                {
                    message.EnsureSequenceNumber(sequenceNumber);
                }
                else
                {
                    Interlocked.Decrement(ref _nextSendSequenceNumber);
                }
            }            
        }

        protected void EnqueuePendingMessage(IMessage message) => _pendingMessages.Enqueue(message);

        protected IEnumerable<IMessage> GetPendingMessages()
        {
            while (_pendingMessages.TryDequeue(out var message))
                yield return message;
        }

        public void ReceiveMessageAck(long sequenceNumber)
        {
            if (!_sendingMessageStates.TryRemove(sequenceNumber, out var state)) return;
            var rawRtt = GetElapsedMilliseconds(state.LastSendTime);
            if (rawRtt > 0)
            {
                var estimated = _rttTracker.Estimated;
                var clamped = Math.Clamp(rawRtt, estimated / RttClampRangeFactor, estimated * RttClampRangeFactor);
                if (rawRtt > clamped)
                    OnRttSpikeDetected(sequenceNumber, rawRtt, estimated);

                _rttTracker.Update(clamped);
            }

            state.UpdateTimeout(_rttTracker.Estimated);
        }

        private static double GetElapsedMilliseconds(long startTimestamp)
            => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        
        protected virtual void OnRttSpikeDetected(long sequenceNumber, double rawRtt, double estimatedRtt)
        {
        }
        
        protected IEnumerable<IMessage> GetTimedOutMessages()
        {
            var result = new List<IMessage>();

            foreach (var (key, state) in _sendingMessageStates)
            {
                if (!state.HasExpired)
                    continue;

                if (state.ReSendCnt < state.MaxReSendCnt)
                {
                    state.IncrementReSendCnt();
                    result.Add(state.Message);
                }
                else
                {
                    OnMessageSendFailure(state.Message);
                    _sendingMessageStates.TryRemove(key, out _);
                }
            }

            return result;
        }

        protected virtual void OnMessageSendFailure(IMessage message)
        {
        }

        public IEnumerable<IMessage> DrainInOrderReceivedMessages(IMessage message)
        {
            if (message.SequenceNumber <= _expectedReceiveSequenceNumber || !_pendingReceiveMessages.TryAdd(message.SequenceNumber, message))
            {
                // 이미 처리했거나 대기 중인 메시지임
                yield break;
            }
            
            while (_pendingReceiveMessages.TryRemove(_expectedReceiveSequenceNumber + 1, out var pending))
            {
                // 순서대로 메시지 처리함
                _expectedReceiveSequenceNumber++;
                yield return pending;
            }
        }

        protected void ResetAllSendingStates()
        {
            foreach (var state in _sendingMessageStates.Values)
                state.Reset();                
        }

        protected void ResetMessageProcessor()
        {
            _nextSendSequenceNumber = 0;
            _expectedReceiveSequenceNumber = 0;
            _sendingMessageStates.Clear();
            _pendingReceiveMessages.Clear();
        }
    }
}
