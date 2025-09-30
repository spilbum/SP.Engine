using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using SP.Common;

namespace SP.Engine.Runtime.Networking
{
    public abstract class ReliableMessageProcessor
    {
        private sealed class OutboundMessageState
        {
            private int _InitialRtoMs;
            public IMessage Message { get; }
            public int MaxRetryCount { get; }
            public int RetryCount { get; private set; }
            public DateTime ExpiredAtUtc { get; private set; }
            public int RtoMs { get; private set; }

            public OutboundMessageState(IMessage message, int initialRtoMs, int maxRetryCount)
            {
                Message = message;
                MaxRetryCount = maxRetryCount;
                RtoMs = initialRtoMs;
                RetryCount = 0;
                _InitialRtoMs = initialRtoMs;
                RefreshExpire();
            }
            
            public void IncrementRetry(int rtoMs)
            {
                RetryCount++;
                RtoMs = rtoMs;
                RefreshExpire();
            }

            public void Reset()
            {
                RetryCount = 0;
                RtoMs = _InitialRtoMs;
                RefreshExpire();
            }
            
            public bool HasExpired => DateTime.UtcNow >= ExpiredAtUtc;
            public bool HasReachedRetryLimit => RetryCount >= MaxRetryCount;
            private void RefreshExpire() => ExpiredAtUtc = DateTime.UtcNow.AddMilliseconds(RtoMs);
        }

        private sealed class RtoCalculator
        {
            private readonly EwmaFilter _srtt = new EwmaFilter(0.125);
            private readonly EwmaFilter _rttVar = new EwmaFilter(0.25);
            
            private const int RtoClockGranularityMs = 10;
            private const int MinRtoMs = 200;
            private const int MaxRtoMs = 5000;
        
            public void AddSample(double rttMs)
            {
                if (!_srtt.IsInitialized)
                {
                    _srtt.Update(rttMs);
                    _rttVar.Update(rttMs / 2.0);
                    return;
                }

                var delta = Math.Abs(_srtt.Value - rttMs);
                _rttVar.Update(delta);
                _srtt.Update(rttMs);
            }

            public int GetRtoMs()
            {
                var rto = _srtt.Value + Math.Max(RtoClockGranularityMs, 4 * _rttVar.Value);
                return (int)Math.Clamp(rto, MinRtoMs, MaxRtoMs);
            }

            public void Reset()
            {
                _srtt.Reset();
                _rttVar.Reset();
            }
        }
        
        private readonly ConcurrentQueue<IMessage> _pendingMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentDictionary<long, OutboundMessageState> _messageStates = new ConcurrentDictionary<long, OutboundMessageState>();
        private readonly ConcurrentDictionary<long, IMessage> _outOfOrderMessages = new ConcurrentDictionary<long, IMessage>();
        private readonly RtoCalculator _rtoCalculator = new RtoCalculator();
        private readonly object _receiveLock = new object();
        private const int RecentSeqWindow = 128;
        private long _nextReliableSeq;
        private long _nextExpectedSeq = 1;
        private readonly HashSet<long> _recentSeqs = new HashSet<long>();
        private readonly Queue<long> _recentSeqsQueue = new Queue<long>();
        
        protected int SendTimeoutMs { get; private set; } = 500;
        protected int MaxRetryCount { get; private set; } = 5;
        protected void SetSendTimeoutMs(int ms) => SendTimeoutMs = ms;
        protected void SetMaxRetryCount(int cnt) => MaxRetryCount = cnt;
        protected long GetNextReliableSeq() => Interlocked.Increment(ref _nextReliableSeq);
        protected void EnqueuePendingMessage(IMessage message) => _pendingMessageQueue.Enqueue(message);
        
        protected IEnumerable<IMessage> DequeuePendingMessages()
        {
            while (_pendingMessageQueue.TryDequeue(out var message))
                yield return message;
        }

        protected bool RegisterMessageState(IMessage message)
        {
            if (message.SequenceNumber <= 0) return false;   
            var state = new OutboundMessageState(message, SendTimeoutMs, MaxRetryCount);
            return _messageStates.TryAdd(message.SequenceNumber, state);
        }

        protected bool RemoveMessageState(long sequenceNumber)
            => _messageStates.TryRemove(sequenceNumber, out _);

        protected void AddRttSample(double rttMs)
        {
            _rtoCalculator.AddSample(rttMs);
        }

        protected IEnumerable<IMessage> FindExpiredForRetry()
        {
            foreach (var (seq, state) in _messageStates)
            {
                if (!state.HasExpired)
                    continue;

                if (state.HasReachedRetryLimit)
                {
                    _messageStates.TryRemove(seq, out _);
                    OnRetryLimitExceeded(state.Message);
                    continue;
                }
                
                state.IncrementRetry(_rtoCalculator.GetRtoMs());
                OnDebug($"Retrying message: seq={seq}, retry={state.RetryCount}/{state.MaxRetryCount}, expiresAt={state.ExpiredAtUtc:HH:mm:ss.fff}, rtoMs={state.RtoMs}");
                yield return state.Message;
            }
        }

        public IEnumerable<IMessage> ProcessMessageInOrder(IMessage message)
        {
            if (message.SequenceNumber == 0)
            {
                yield return message;
                yield break;
            }
            
            lock (_receiveLock)
            {
                if (!TryAcceptSequence(message.SequenceNumber))
                    yield break;

                if (message.SequenceNumber == _nextExpectedSeq)
                {
                    foreach (var msg in EmitInOrder(message))
                        yield return msg;
                }
                else
                {
                    _outOfOrderMessages.TryAdd(message.SequenceNumber, message);
                }
            }
        }
        
        private bool TryAcceptSequence(long seq)
        {
            // 과거 시퀀스 거부
            if (seq < _nextExpectedSeq)
                return false;
            
            // 종복 시퀀스 거부
            if (_recentSeqs.Contains(seq))
                return false;

            // LRU 창 유지
            if (_recentSeqsQueue.Count >= RecentSeqWindow)
            {
                var old = _recentSeqsQueue.Dequeue();
                _recentSeqs.Remove(old);
            }
            
            // 신규 시퀀스
            _recentSeqsQueue.Enqueue(seq);
            _recentSeqs.Add(seq);
            return true;
        }

        private IEnumerable<IMessage> EmitInOrder(IMessage firstMessage)
        {
            yield return firstMessage;
            _nextExpectedSeq++;

            while (_outOfOrderMessages.TryRemove(_nextExpectedSeq, out var nextMessage))
            {
                yield return nextMessage;
                _nextExpectedSeq++;
            }
        }

        protected void ResetAllMessageStates()
        {
            foreach (var kvp in _messageStates)
                kvp.Value.Reset();
        }

        protected void ResetProcessorState()
        {
            _rtoCalculator.Reset();
            _messageStates.Clear();
            _pendingMessageQueue.Clear();

            lock (_receiveLock)
            {
                _outOfOrderMessages.Clear();
                _nextExpectedSeq = 1;
                _recentSeqsQueue.Clear();
                _recentSeqs.Clear();
            }
        }

        protected abstract void OnDebug(string format, params object[] args);
        protected abstract void OnRetryLimitExceeded(IMessage message);
    }
}
