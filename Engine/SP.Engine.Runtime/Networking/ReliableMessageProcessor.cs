using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public sealed class ReliableSender : IDisposable
    {
        private readonly object _lock = new object();

        private readonly SortedDictionary<uint, RetransmissionTracker> _states =
            new SortedDictionary<uint, RetransmissionTracker>();

        private readonly SortedSet<RetransmissionTracker> _timeoutOrder = new SortedSet<RetransmissionTracker>(new TrackerComparer());
        private readonly RetransmissionTrackerPool _pool = new RetransmissionTrackerPool();
        private bool _disposed;

        public int MessageCount
        {
            get
            {
                lock (_lock) return _states.Count;
            }
        }

        public void Register(TcpMessage message, int initRtoMs, int maxAttempts)
        {
            lock (_lock)
            {
                if (_disposed) return;

                var state = _pool.Rent(message, initRtoMs, maxAttempts);
                state.Message.Retain();

                _states.Add(message.SequenceNumber, state);
                _timeoutOrder.Add(state);
            }
        }

        public void RemoveUntil(uint remoteAckNumber)
        {
            lock (_lock)
            {
                if (_disposed) return;

                while (_states.Count > 0)
                {
                    // 가장 작은 시퀀스 번호 확인
                    var firstKey = _states.Keys.First(); 
            
                    // remoteAckNumber보다 작으면 제거 대상 (Unsigned overflow 고려)
                    if ((int)(firstKey - remoteAckNumber) >= 0) break;

                    if (!_states.Remove(firstKey, out var state)) continue;
                    _timeoutOrder.Remove(state);
                    state.Message.Dispose();
                    _pool.Return(state);
                }
            }
        }

        public (List<TcpMessage> Retries, List<TcpMessage> Failed) ProcessRetransmissions(RtoEstimator rto)
        {
            var now = DateTime.UtcNow;
            List<TcpMessage> retries = null;
            List<TcpMessage> failed = null;

            lock (_lock)
            {
                if (_disposed) return default;

                while (_timeoutOrder.Count > 0)
                {
                    var minTracker = _timeoutOrder.Min;
                    if (!minTracker.IsTimeout(now)) break;

                    _timeoutOrder.Remove(minTracker);

                    if (minTracker.IsExhausted)
                    {
                        failed ??= new List<TcpMessage>();
                        failed.Add(minTracker.Message);
                        _states.Remove(minTracker.Message.SequenceNumber);
                        _pool.Return(minTracker);
                    }
                    else
                    {
                        var nextRto = rto.GetRtoMs(minTracker.CurrentAttempt);
                        minTracker.RecordRetryAttempt(nextRto);
                        
                        retries ??= new List<TcpMessage>();
                        retries.Add(minTracker.Message);

                        _timeoutOrder.Add(minTracker);
                    }
                }
            }

            return (retries, failed);
        }

        public void RestartAll()
        {
            lock (_lock)
            {
                if (_disposed || _states.Count == 0) return;
                _timeoutOrder.Clear();
                
                foreach (var state in _states.Values)
                {
                    state.Restart();
                    _timeoutOrder.Add(state);
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var state in _states.Values) state.Message.Dispose();
                _states.Clear();
            }
        }

        private class TrackerComparer : IComparer<RetransmissionTracker>
        {
            public int Compare(RetransmissionTracker x, RetransmissionTracker y)
            {
                var result = x!.NextTimeoutUtc.CompareTo(y!.NextTimeoutUtc);
                return result == 0 
                    ? x.Message.SequenceNumber.CompareTo(y.Message.SequenceNumber) 
                    : result;
            }
        }

        private class RetransmissionTrackerPool
        {
            private readonly Stack<RetransmissionTracker> _pool = new Stack<RetransmissionTracker>();
            private readonly object _lock = new object();

            public RetransmissionTracker Rent(TcpMessage message, int rto, int maxAttempts)
            {
                lock (_lock)
                {
                    if (_pool.Count <= 0)
                        return new RetransmissionTracker(message, rto, maxAttempts);
                    
                    var t = _pool.Pop();
                    t.Reset(message, rto, maxAttempts);
                    return t;
                }
            }

            public void Return(RetransmissionTracker tracker)
            {
                tracker.Clear();
                lock (_lock) { _pool.Push(tracker); }
            }   
        }
        
        private class RetransmissionTracker
        {
            public TcpMessage Message { get; private set; }
            public int MaxAttempts { get; private set; }
            public int CurrentAttempt { get; private set; }
            public int CurrentRtoMs { get; private set; }
            public DateTime NextTimeoutUtc { get; private set; }

            public RetransmissionTracker(TcpMessage message, int initRtoMs, int maxAttempts)
            {
                Message = message;
                CurrentRtoMs = initRtoMs;
                MaxAttempts = maxAttempts;
                CurrentAttempt = 0;
                ScheduleNextTimeout();
            }
            
            public bool IsTimeout(DateTime nowUtc) => nowUtc >= NextTimeoutUtc;
            public bool IsExhausted => CurrentAttempt >= MaxAttempts;

            public void RecordRetryAttempt(int nextRtoMs)
            {
                CurrentAttempt++;
                CurrentRtoMs = nextRtoMs;
                ScheduleNextTimeout();
            }

            private void ScheduleNextTimeout()
            {
                NextTimeoutUtc = DateTime.UtcNow.AddMilliseconds(CurrentRtoMs);
            }

            public void Restart()
            {
                CurrentAttempt = 0;
                ScheduleNextTimeout();
            }

            public void Reset(TcpMessage message, int initRtoMs, int maxAttempts)
            {
                Message = message;
                CurrentAttempt = 0;
                CurrentRtoMs = initRtoMs;
                MaxAttempts = maxAttempts;
                ScheduleNextTimeout();
            }

            public void Clear()
            {
                CurrentAttempt = 0;
            }
        }
    }

    public sealed class RtoEstimator
    {
        private readonly int _minRtoMs;
        private readonly int _maxRtoMs;
        private readonly int _minRtoVarianceMs;
        private double _lastRtt;
        
        private EwmaFilter _rttVar = new EwmaFilter(0.25);
        private EwmaFilter _rttFilter = new EwmaFilter(0.125);

        public double SRttMs => _rttFilter.Value;
        public double JitterMs => _rttVar.Value;

        public RtoEstimator(int minRtoMs = 200, int maxRtoMs = 5000, int minRtoVarianceMs = 100)
        {
            if (minRtoMs < 10) throw new ArgumentException("RTO estimator minRtoMs must be >= 50");
            if (maxRtoMs < minRtoMs) throw new ArgumentException("RTO estimator maxRtoMs must be >= minRtoMs");
            
            _minRtoMs = minRtoMs;
            _maxRtoMs = maxRtoMs;
            _minRtoVarianceMs = minRtoVarianceMs;
        }

        public void AddSample(double rttMs)
        {
            if (!_rttFilter.IsInitialized)
            {
                _rttFilter.Update(rttMs);
                _rttVar.Update(rttMs/ 2);
            }
            else
            {
                // RTTVAR = (1 - beta) * RTTVAR + beta * |SRTT - R'|
                var delta = Math.Abs(_rttFilter.Value - rttMs);
                _rttVar.Update(delta);
                _rttFilter.Update(rttMs);
            }
            
            _lastRtt = rttMs;
        }

        public int GetRtoMs(int retryCount)
        {
            // RTO = SRTT + max(G, K*RTTVAR)  (K = 4)
            var baseRto = _rttFilter.Value + Math.Max(_minRtoVarianceMs, 4 * _rttVar.Value);
            var safeRetryCount = Math.Min(retryCount, 10);
            var backoffRto = baseRto * (1 << safeRetryCount); 
            return (int)Math.Clamp(backoffRto, _minRtoMs, _maxRtoMs);
        }
    }

    public sealed class SequenceReorderer : IDisposable
    {
        private readonly object _lock = new object();
        private uint _nextExpectedSeq = 1;
        private readonly SortedDictionary<uint, TcpMessage> _buffer = new SortedDictionary<uint, TcpMessage>();
        private bool _disposed;
        
        public int TotalOutOfOrderCount
        {
            get
            {
                lock (_lock) return _buffer.Count;
            }
        }

        public uint NextExpectedSeq
        {
            get
            {
                lock (_lock) return _nextExpectedSeq;
            }
        }

        public List<TcpMessage> Push(TcpMessage message, int maxBufferSize)
        {
            if (_disposed) return null;
            
            lock (_lock)
            {
                if (message.SequenceNumber < _nextExpectedSeq || _buffer.ContainsKey(message.SequenceNumber))
                    return null;

                if (_buffer.Count >= maxBufferSize)
                {
                    var oldestSeq = _buffer.Keys.First();
                    _nextExpectedSeq = oldestSeq;
                }
                    
                if (_buffer.TryAdd(message.SequenceNumber, message))
                    message.Retain();

                return DrainReadyMessages();
            }
        }

        private List<TcpMessage> DrainReadyMessages()
        {
            List<TcpMessage> readyList = null;

            while (_buffer.Remove(_nextExpectedSeq, out var message))
            {
                readyList ??= new List<TcpMessage>();
                readyList.Add(message);
                _nextExpectedSeq++;
            }
            
            return readyList;
        }
        
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var message in _buffer.Values) message.Dispose();
                _buffer.Clear();   
            }
        }
    }

    public class ReliableMessageProcessor : IDisposable
    {
        private readonly SwapQueue<TcpMessage> _pendingQueue = new SwapQueue<TcpMessage>(1024);
        private readonly ReliableSender _sender = new ReliableSender();
        private readonly SequenceReorderer reorderer = new SequenceReorderer();
        private readonly RtoEstimator _rtoEstimator = new RtoEstimator();
        private readonly List<TcpMessage> _dequeuedCache = new List<TcpMessage>();
        private long _nextReliableSeq;
        private int _maxOutOfOrder = 32;
        private int _maxRetransmissionCount = 5;
        private int _initSendTimeoutMs = 500;

        public int InFlightCount => _sender.MessageCount;
        public int OutOfOrderCount => reorderer.TotalOutOfOrderCount;
        public int PendingCount => _pendingQueue.Count;
        public double SRttMs => _rtoEstimator.SRttMs;
        public double JitterMs => _rtoEstimator.JitterMs;
        
        public int MaxAckDelayMs { get; private set; } = 30;
        public int AckStepThreshold { get; private set; } = 10;
        public uint LastReceivedSeq => reorderer.NextExpectedSeq;

        public void SetSendTimeoutMs(int ms) => _initSendTimeoutMs = ms;
        public void SetMaxRetransmissionCount(int count) => _maxRetransmissionCount = count;
        public void SetMaxAckDelayMs(int ms) => MaxAckDelayMs = ms;
        public void SetAckStepThreshold(int count) => AckStepThreshold = count;
        
        public void SetMaxOutOfOrder(int count) => _maxOutOfOrder = count;
        
        public void EnqueuePendingMessage(TcpMessage message)
        {
            if (_pendingQueue.TryEnqueue(message))
            {
                message.Retain();
            }
            else
            {
                message.Dispose();
            }
        }

        public List<TcpMessage> DequeuePendingMessages()
        {
            _dequeuedCache.Clear();
            _pendingQueue.Exchange(_dequeuedCache);
            return _dequeuedCache;
        }

        public void PrepareReliableSend(TcpMessage message)
        {
            var seqNum = (uint)Interlocked.Increment(ref _nextReliableSeq);
            var ackNum = reorderer.NextExpectedSeq;
            message.SetSequenceNumber(seqNum);
            message.SetAckNumber(ackNum);
            _sender.Register(message, _initSendTimeoutMs, _maxRetransmissionCount);
        }

        public void AcknowledgeInFlight(uint remoteAckNumber)
        {
            _sender.RemoveUntil(remoteAckNumber);
        }

        public (List<TcpMessage> Retries, List<TcpMessage> Failed) ProcessRetransmissions()
            => _sender.ProcessRetransmissions(_rtoEstimator);

        public List<TcpMessage> IngestReceivedMessage(TcpMessage message)
        {
            return reorderer.Push(message, _maxOutOfOrder);
        }

        public void RestartInFlightMessages()
        {
            _sender.RestartAll();
        }
        
        public void AddRtoSample(double rttMs) => _rtoEstimator.AddSample(rttMs);

        public void Dispose()
        {
            var remaining = new List<TcpMessage>();
            _pendingQueue.Exchange(remaining);
            foreach (var message in remaining) message.Dispose();
            
            _pendingQueue.Dispose();
            _sender.Dispose();
            reorderer.Dispose();
        }
    }
}
