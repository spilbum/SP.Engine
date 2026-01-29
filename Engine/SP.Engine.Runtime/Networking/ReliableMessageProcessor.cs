using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using SP.Core;
using SP.Core.Logging;

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
        bool TryGetRetryMessages(IRtoEstimator rto, out List<IMessage> retries);
        void ResetAll();
        void Clear();
    }

    public interface IOrderingBuffer
    {
        List<IMessage> ProcessInOrder(IMessage message);
        void Reset();
    }

    public sealed class MessageTracker : IMessageTracker
    {
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<long, State> _states = new ConcurrentDictionary<long, State>();

        public MessageTracker(ILogger logger)
        {
            _logger = logger;
        }

        public bool Register(IMessage message, int initialRtoMs, int maxRetryCount)
        {
            if (!(message is TcpMessage msg) || msg.SequenceNumber == 0) return false;
            var state = new State(msg, initialRtoMs, maxRetryCount);
            return _states.TryAdd(msg.SequenceNumber, state);
        }

        public bool Remove(long sequenceNumber)
        {
            return _states.TryRemove(sequenceNumber, out _);
        }

        public bool TryGetRetryMessages(IRtoEstimator rto, out List<IMessage> retries)
        {
            retries = new List<IMessage>();
            foreach (var (seq, state) in _states)
            {
                if (!state.HasExpired) continue;
                if (state.HasReachedRetryLimit)
                {
                    _states.TryRemove(seq, out _);
                    _logger.Error("Retry message expired. seq={0}, id={1}, retry={1}/{2}",
                        seq, state.Message.Id, state.RetryCount, state.MaxRetryCount);

                    retries.Clear();
                    return false;
                }

                var rtoMs = rto.GetRtoMs();
                state.IncrementRetry(rtoMs);
                _logger.Debug(
                    $"Retrying message: seq={seq}, retry={state.RetryCount}/{state.MaxRetryCount}, rtoMs={state.RtoMs}");
                retries.Add(state.Message);
            }

            return true;
        }

        public void ResetAll()
        {
            foreach (var kv in _states) kv.Value.Reset();
        }

        public void Clear()
        {
            _states.Clear();
        }

        private class State
        {
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

            public IMessage Message { get; }
            public int MaxRetryCount { get; }
            public int RetryCount { get; private set; }
            public int RtoMs { get; private set; }

            public bool HasExpired => DateTime.UtcNow >= _expiresAtUtc;
            public bool HasReachedRetryLimit => RetryCount >= MaxRetryCount;

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

            private void Refresh()
            {
                _expiresAtUtc = DateTime.UtcNow.AddMilliseconds(RtoMs);
            }
        }
    }

    public sealed class RtoEstimator : IRtoEstimator
    {
        private const int RtoClockGranularityMs = 10;
        private const int MinRtoMs = 200;
        private const int MaxRtoMs = 5000;
        private readonly EwmaFilter _rttVar = new EwmaFilter(0.25);
        private readonly EwmaFilter _smoothedRtt = new EwmaFilter(0.125);

        public void AddSample(double rttMs)
        {
            if (!_smoothedRtt.IsInitialized)
            {
                _smoothedRtt.Update(rttMs);
                _rttVar.Update(rttMs / 2.0);
                return;
            }

            var delta = Math.Abs(_smoothedRtt.Value - rttMs);
            _rttVar.Update(delta);
            _smoothedRtt.Update(rttMs);
        }

        public int GetRtoMs()
        {
            var rto = _smoothedRtt.Value + Math.Max(RtoClockGranularityMs, 4 * _rttVar.Value);
            return (int)Math.Clamp(rto, MinRtoMs, MaxRtoMs);
        }

        public void Reset()
        {
            _smoothedRtt.Reset();
            _rttVar.Reset();
        }
    }

    public sealed class OrderingBuffer : IOrderingBuffer
    {
        private const int RecentSeqWindow = 128;
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<long, IMessage> _outOfOrder = new ConcurrentDictionary<long, IMessage>();
        private readonly Queue<long> _recentSeqQueue = new Queue<long>();

        private readonly HashSet<long> _recentSeqs = new HashSet<long>();
        private long _nextExpectedSeq = 1;

        public List<IMessage> ProcessInOrder(IMessage message)
        {
            var list = new List<IMessage>();
            if (!(message is TcpMessage tcp) || tcp.SequenceNumber == 0)
            {
                list.Add(message);
                return list;
            }

            lock (_lock)
            {
                if (!TryAcceptSequence(tcp.SequenceNumber))
                    return list;

                if (tcp.SequenceNumber == _nextExpectedSeq)
                    list.AddRange(EmitInOrder(message));
                else
                    _outOfOrder.TryAdd(tcp.SequenceNumber, message);
            }

            return list;
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

        private List<IMessage> EmitInOrder(IMessage first)
        {
            var list = new List<IMessage> { first };
            while (_outOfOrder.TryRemove(++_nextExpectedSeq, out var next)) list.Add(next);
            return list;
        }
    }

    public class ReliableMessageProcessor
    {
        private const int PendingBufferWindow = 128;
        private readonly List<IMessage> _dequeued = new List<IMessage>();
        private readonly SwapBuffer<IMessage> _pendingBuffer;
        private readonly IOrderingBuffer _ordering;
        private readonly IRtoEstimator _rto;
        private readonly IMessageTracker _tracker;
        private long _nextReliableSeq;

        public ReliableMessageProcessor(ILogger logger)
            : this(new SwapBuffer<IMessage>(PendingBufferWindow), new MessageTracker(logger),
                new OrderingBuffer(), new RtoEstimator())
        {
        }

        public ReliableMessageProcessor(
            SwapBuffer<IMessage> pendingBuffer, IMessageTracker tracker, IOrderingBuffer ordering, IRtoEstimator rto)
        {
            _pendingBuffer = pendingBuffer;
            _tracker = tracker;
            _ordering = ordering;
            _rto = rto;
        }

        public int SendTimeoutMs { get; private set; } = 500;
        public int MaxRetryCount { get; private set; } = 5;

        public void SetSendTimeoutMs(int ms)
        {
            SendTimeoutMs = ms;
        }

        public void SetMaxRetryCount(int count)
        {
            MaxRetryCount = count;
        }

        public long GetNextReliableSeq()
        {
            return Interlocked.Increment(ref _nextReliableSeq);
        }

        public bool EnqueuePendingMessage(IMessage message)
        {
            return _pendingBuffer.TryWrite(message);
        }

        public IEnumerable<IMessage> DequeuePendingMessages()
        {
            _dequeued.Clear();
            _pendingBuffer.Flush(_dequeued);
            foreach (var msg in _dequeued)
                yield return msg;
        }

        public bool RegisterMessageState(IMessage message)
        {
            return _tracker.Register(message, SendTimeoutMs, MaxRetryCount);
        }

        public bool RemoveMessageState(long sequenceNumber)
        {
            return _tracker.Remove(sequenceNumber);
        }

        public void AddRtoSample(double rttMs)
        {
            _rto.AddSample(rttMs);
        }

        public bool TryGetRetryMessages(out List<IMessage> retries)
        {
            return _tracker.TryGetRetryMessages(_rto, out retries);
        }

        public List<IMessage> ProcessMessageInOrder(IMessage message)
        {
            return _ordering.ProcessInOrder(message);
        }

        public void ResetAllMessageStates()
        {
            _tracker.ResetAll();
        }

        public void Reset()
        {
            _rto.Reset();
            _tracker.Clear();
            _ordering.Reset();
            _nextReliableSeq = 0;
        }
    }
}
