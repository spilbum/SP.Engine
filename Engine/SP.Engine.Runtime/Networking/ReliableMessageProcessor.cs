using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using SP.Common;

namespace SP.Engine.Runtime.Networking
{
    public interface IRtoEstimator
    {
        void AddSample(double rttMs);
        int GetRtoMs();
        void Reset();
    }
    
    public interface IMessageTracker
    {
        bool Register(IMessage message, int initialRtoMs, int maxRetryCount);
        bool Remove(long sequenceNumber);
        IEnumerable<IMessage> CollectExpiredForRetry(IRtoEstimator rto, IRetryCallback callback = null);
        void ResetAll();
        void Clear();
    }

    public interface IRetryCallback
    {
        void OnDebug(string format, params object[] args);
        void OnRetryLimitExceeded(IMessage message, int count, int maxCount);
    }

    public interface IOrderingBuffer
    {
        IEnumerable<IMessage> ProcessInOrder(IMessage message);
        void Reset();
    }
    
    public sealed class MessageTracker : IMessageTracker
    {
        private class State
        {
            public IMessage Message { get; }
            public int MaxRetryCount { get; }
            public int RetryCount { get; private set; }
            public int RtoMs { get; private set; }
            private readonly int _initialRtoMs;
            private DateTime _expiresAtUtc;

            public State(IMessage message, int initialRtoMs, int maxRetryCount)
            {
                Message = message;
                _initialRtoMs = initialRtoMs;
                RtoMs = initialRtoMs;
                MaxRetryCount = maxRetryCount;
                RetryCount = 0;
                Refresh();
            }
            
            public void IncrementRetry(int rtoMs)
            {
                RetryCount++;
                RtoMs = rtoMs;
                Refresh();
            }

            public void Reset()
            {
                RetryCount = 0;
                RtoMs = _initialRtoMs;
                Refresh();
            }
            
            public bool HasExpired => DateTime.UtcNow >= _expiresAtUtc;
            public bool HasReachedRetryLimit => RetryCount >= MaxRetryCount;
            private void Refresh() => _expiresAtUtc = DateTime.UtcNow.AddMilliseconds(RtoMs);
        }
        
        private readonly ConcurrentDictionary<long, State> _states = new ConcurrentDictionary<long, State>();

        public bool Register(IMessage message, int initialRtoMs, int maxRetryCount)
        {
            if (!(message is TcpMessage msg) || msg.SequenceNumber == 0) return false;
            var state = new State(msg, initialRtoMs, maxRetryCount);
            return _states.TryAdd(msg.SequenceNumber, state);
        }
        
        public bool Remove(long sequenceNumber) => _states.TryRemove(sequenceNumber, out _);

        public IEnumerable<IMessage> CollectExpiredForRetry(IRtoEstimator rtoEstimator, IRetryCallback callback = null)
        {
            foreach (var (seq, state) in _states)
            {
                if (!state.HasExpired) continue;
                if (state.HasReachedRetryLimit)
                {
                    _states.TryRemove(seq, out _);
                    callback?.OnRetryLimitExceeded(state.Message, state.RetryCount, state.MaxRetryCount);
                    continue;
                }
                
                var rtoMs = rtoEstimator.GetRtoMs();
                state.IncrementRetry(rtoMs);
                callback?.OnDebug($"Retrying message: seq={seq}, retry={state.RetryCount}/{state.MaxRetryCount}, rtoMs={state.RtoMs}");
                yield return state.Message;
            }
        }

        public void ResetAll()
        {
            foreach (var kv in _states) kv.Value.Reset();
        }
        
        public void Clear() => _states.Clear();
    }

    public sealed class RtoEstimator : IRtoEstimator
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

    public sealed class OrderingBuffer : IOrderingBuffer
    {
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<long, IMessage> _outOfOrder = new ConcurrentDictionary<long, IMessage>();
        private const int RecentSeqWindow = 128;
        private long _nextExpectedSeq = 1;
        
        private readonly HashSet<long> _recentSeqs = new HashSet<long>();
        private readonly Queue<long> _recentSeqQueue = new Queue<long>();

        public IEnumerable<IMessage> ProcessInOrder(IMessage message)
        {
            if (!(message is TcpMessage tcp) || tcp.SequenceNumber == 0)
            {
                yield return message;
                yield break;
            }
            
            lock (_lock)
            {
                if (!TryAcceptSequence(tcp.SequenceNumber))
                    yield break;

                if (tcp.SequenceNumber == _nextExpectedSeq)
                {
                    foreach (var msg in EmitInOrder(message))
                        yield return msg;
                }
                else
                {
                    _outOfOrder.TryAdd(tcp.SequenceNumber, message);
                }
            }
        }

        private bool TryAcceptSequence(long sequenceNumber)
        {
            if (sequenceNumber < _nextExpectedSeq) return false;
            if (!_recentSeqs.Add(sequenceNumber)) return false;

            if (_recentSeqQueue.Count >= RecentSeqWindow)
            {
                var old = _recentSeqQueue.Dequeue();
                _recentSeqs.Remove(old);
            }
            _recentSeqQueue.Enqueue(sequenceNumber);
            return true;
        }

        private IEnumerable<IMessage> EmitInOrder(IMessage first)
        {
            yield return first;
            _nextExpectedSeq++;

            while (_outOfOrder.TryRemove(_nextExpectedSeq, out var next))
            {
                yield return next;
                _nextExpectedSeq++;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _outOfOrder.Clear();
                _nextExpectedSeq = 1;
                _recentSeqQueue.Clear();
                _recentSeqs.Clear();
            }
        }
    }
    
    public abstract class ReliableMessageProcessor
    {
        private readonly IBatchQueue<IMessage> _pendingQueue;
        private readonly IMessageTracker _tracker;
        private readonly IOrderingBuffer _ordering;
        private readonly IRtoEstimator _rto;

        protected int SendTimeoutMs { get; private set; } = 500;
        protected int MaxRetryCount { get; private set; } = 5;
        private long _nextReliableSeq;
        private const int PendingQueueWindow = 128;
        private readonly List<IMessage> _messages = new List<IMessage>();

        protected ReliableMessageProcessor()
            : this(new ConcurrentBatchQueue<IMessage>(PendingQueueWindow), new MessageTracker(), new OrderingBuffer(), new RtoEstimator())
        {
            
        }
        
        protected ReliableMessageProcessor(
            IBatchQueue<IMessage> pendingQueue, IMessageTracker tracker, IOrderingBuffer ordering, IRtoEstimator rto)
        {
            _pendingQueue = pendingQueue;
            _tracker = tracker;
            _ordering = ordering;
            _rto = rto;
        }
        
        protected void SetSendTimeoutMs(int ms) => SendTimeoutMs = ms;
        protected void SetMaxRetryCount(int count) => MaxRetryCount = count;
        protected long GetNextReliableSeq() => Interlocked.Increment(ref _nextReliableSeq);

        protected bool EnqueuePendingMessage(IMessage message)
        {
            if (_pendingQueue.Enqueue(message)) return true;
            _pendingQueue.Resize(_pendingQueue.Capacity * 2);
            return _pendingQueue.Enqueue(message);
        }

        protected IEnumerable<IMessage> DequeuePendingMessages()
        {
            _messages.Clear();
            _pendingQueue.DequeueAll(_messages);
            foreach (var msg in _messages)
                yield return msg;
        }
        
        protected bool RegisterMessageState(IMessage message)
            => _tracker.Register(message, SendTimeoutMs, MaxRetryCount);

        protected bool RemoveMessageState(long sequenceNumber) => _tracker.Remove(sequenceNumber);
        
        protected void AddRtoSample(double rttMs) => _rto.AddSample(rttMs);

        protected IEnumerable<IMessage> FindExpiredForRetry()
            => _tracker.CollectExpiredForRetry(_rto, new RetryCallbackAdaptor(this));
        
        public IEnumerable<IMessage> ProcessMessageInOrder(IMessage message)
            => _ordering.ProcessInOrder(message);

        protected void ResetAllMessageStates() => _tracker.ResetAll();

        protected void ResetProcessorState()
        {
            _rto.Reset();
            _tracker.Clear();
            _ordering.Reset();
        }

        private sealed class RetryCallbackAdaptor : IRetryCallback
        {
            private readonly ReliableMessageProcessor _p;
            public RetryCallbackAdaptor(ReliableMessageProcessor processor) => _p = processor;

            public void OnRetryLimitExceeded(IMessage message, int count, int maxCount) 
                => _p.OnRetryLimitExceeded(message, count, maxCount);
            public void OnDebug(string message, params object[] args) => _p.OnDebug(message, args);
        }
        
        protected abstract void OnDebug(string format, params object[] args);
        protected abstract void OnRetryLimitExceeded(IMessage message, int count, int maxCount);
    }
}
