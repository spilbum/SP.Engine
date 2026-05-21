using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public sealed class ReliableSender : IDisposable
    {
        private const int WindowSize = 4096;
        private const int WindowSizeMask = WindowSize - 1;
        
        private readonly object _lock = new object();
        
        private readonly RetransmissionTracker[] _slots = new RetransmissionTracker[WindowSize];
        private readonly RetransmissionTrackerPool _pool = new RetransmissionTrackerPool();

        private int _messageCount;
        private bool _disposed;

        public int MessageCount => Volatile.Read(ref _messageCount);

        public bool Register(TcpMessage message, int initRtoMs, int maxAttempts)
        {
            lock (_lock)
            {
                if (_disposed) return false;

                var seq = message.SequenceNumber;
                var index = (int)(seq & WindowSizeMask);

                if (_slots[index] != null)
                {
                    return false;
                }

                var state = _pool.Rent(message, initRtoMs, maxAttempts);
                state.Message.Retain();

                _slots[index] = state;
                Interlocked.Increment(ref _messageCount);
                return true;
            }
        }

        public void RemoveUntil(uint remoteAckNumber)
        {
            lock (_lock)
            {
                if (_disposed) return;

                for (var i = 0; i < WindowSize; i++)
                {
                    var state = _slots[i];
                    if (state == null || state.IsAcknowledged) continue;

                    if ((int)(state.Message.SequenceNumber - remoteAckNumber) < 0)
                    {
                        state.IsAcknowledged = true;
                    }
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
                if (_disposed) throw new ObjectDisposedException(nameof(ReliableMessageProcessor));

                for (var i = 0; i < WindowSize; i++)
                {
                    var state = _slots[i];
                    if (state == null) continue;

                    if (state.IsAcknowledged)
                    {
                        _slots[i] = null;
                        state.Message.Dispose();
                        _pool.Return(state);
                        Interlocked.Decrement(ref _messageCount);
                        continue;
                    }
                    
                    if (!state.IsTimeout(now)) continue;

                    if (state.IsExhausted)
                    {
                        // 재전송 횟수 초과 폐기
                        _slots[i] = null;
                        failed ??= new List<TcpMessage>();
                        failed.Add(state.Message);
                        
                        _pool.Return(state);
                        Interlocked.Decrement(ref _messageCount);
                    }
                    else
                    {
                        // 재전송 스케줄링 및 RTO 백오프 적용
                        state.RecordRetryAttempt(rto.GetRtoMs(state.CurrentAttempt));
                        retries ??= new List<TcpMessage>();
                        retries.Add(state.Message);
                    }
                }
            }

            return (retries ?? new List<TcpMessage>(), failed ?? new List<TcpMessage>());
        }

        public void RestartAll()
        {
            lock (_lock)
            {
                if (_disposed) return;
                for (var i = 0; i < WindowSize; i++)
                {
                    _slots[i]?.Restart();
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                for (var i = 0; i < WindowSize; i++)
                {
                    var state = _slots[i];
                    if (state == null) continue;
                    state.Message.Dispose();
                    _slots[i] = null;
                }

                _messageCount = 0;
            }
        }

        private class RetransmissionTrackerPool
        {
            private readonly Stack<RetransmissionTracker> _pool = new Stack<RetransmissionTracker>();

            public RetransmissionTracker Rent(TcpMessage message, int rto, int maxAttempts)
            {
                if (_pool.Count <= 0)
                    return new RetransmissionTracker(message, rto, maxAttempts);
                    
                var t = _pool.Pop();
                t.Reset(message, rto, maxAttempts);
                return t;
            }

            public void Return(RetransmissionTracker tracker)
            {
                tracker.Clear();
                _pool.Push(tracker);
            }   
        }
        
        private class RetransmissionTracker
        {
            public TcpMessage Message { get; private set; }
            public int MaxAttempts { get; private set; }
            public int CurrentAttempt { get; private set; }
            public int CurrentRtoMs { get; private set; }
            public DateTime NextTimeoutUtc { get; private set; }
            public bool IsAcknowledged { get; set; }

            public RetransmissionTracker(TcpMessage message, int initRtoMs, int maxAttempts)
            {
                Reset(message, initRtoMs, maxAttempts);
            }
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsTimeout(DateTime nowUtc) => nowUtc >= NextTimeoutUtc;
            public bool IsExhausted => CurrentAttempt >= MaxAttempts;

            public void RecordRetryAttempt(int nextRtoMs)
            {
                CurrentAttempt++;
                CurrentRtoMs = nextRtoMs;
                ScheduleNextTimeout();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ScheduleNextTimeout()
            {
                NextTimeoutUtc = DateTime.UtcNow.AddMilliseconds(CurrentRtoMs);
            }

            public void Restart()
            {
                CurrentAttempt = 0;
                IsAcknowledged = false;
                ScheduleNextTimeout();
            }

            public void Reset(TcpMessage message, int initRtoMs, int maxAttempts)
            {
                Message = message;
                CurrentAttempt = 0;
                CurrentRtoMs = initRtoMs;
                MaxAttempts = maxAttempts;
                IsAcknowledged = false;
                ScheduleNextTimeout();
            }

            public void Clear()
            {
                Message = null;
                IsAcknowledged = false;
            }
        }
    }

    public sealed class RtoEstimator
    {
        private readonly int _minRtoMs;
        private readonly int _maxRtoMs;
        private readonly int _minRtoVarianceMs;

        private readonly EwmaFilter _rttVar = new EwmaFilter(0.25);
        private readonly EwmaFilter _rttFilter = new EwmaFilter(0.125);

        public double SRttMs => _rttFilter.Value;
        public double JitterMs => _rttVar.Value;
        public double LastRttMs { get; private set; }

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
            
            LastRttMs = rttMs;
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
        private int _maxOutOfOrderCount;
        private int _maxRetransmitCount;
        private int _initialRetransmitTimeoutMs;

        public int InFlightCount => _sender.MessageCount;
        public int OutOfOrderCount => reorderer.TotalOutOfOrderCount;
        public int PendingCount => _pendingQueue.Count;
        public double SRttMs => _rtoEstimator.SRttMs;
        public double JitterMs => _rtoEstimator.JitterMs;
        
        public int MaxAckDelayMs { get; private set; }
        public int AckFrequency { get; private set; }
        public uint NextExpectedSeq => reorderer.NextExpectedSeq;

        public void SetMaxRetransmitCount(int count)
        {
            _maxRetransmitCount = count > 0 ? count : 3;
        }

        public void SetInitialRetransmitTimeoutMs(int timeout)
        {
            _initialRetransmitTimeoutMs = timeout > 0 ? timeout : 500;
        }

        public void SetMaxAckDelayMs(int delay)
        {
            MaxAckDelayMs = delay > 0 ? delay : 30;
        }

        public void SetAckFrequency(int frequency)
        {
            AckFrequency = frequency > 0 ? frequency : 10;
        }

        public void SetMaxOutOfOrderCount(int count)
        {
            _maxOutOfOrderCount = count > 0 ? count : 100;
        }
        
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
            _pendingQueue.Extract(_dequeuedCache);
            return _dequeuedCache;
        }

        public void PrepareReliableSend(TcpMessage message)
        {
            var seqNum = (uint)Interlocked.Increment(ref _nextReliableSeq);
            var ackNum = reorderer.NextExpectedSeq;
            message.SetSequenceNumber(seqNum);
            message.SetAckNumber(ackNum);
            _sender.Register(message, _initialRetransmitTimeoutMs, _maxRetransmitCount);
        }

        public void AcknowledgeInFlight(uint remoteAckNumber)
        {
            _sender.RemoveUntil(remoteAckNumber);
        }

        public (List<TcpMessage> Retries, List<TcpMessage> Failed) ProcessRetransmissions()
            => _sender.ProcessRetransmissions(_rtoEstimator);

        public List<TcpMessage> IngestReceivedMessage(TcpMessage message)
        {
            return reorderer.Push(message, _maxOutOfOrderCount);
        }

        public void RestartInFlightMessages()
        {
            _sender.RestartAll();
        }
        
        public void AddRtoSample(double rttMs) => _rtoEstimator.AddSample(rttMs);

        public void Dispose()
        {
            var remaining = new List<TcpMessage>();
            _pendingQueue.Extract(remaining);
            foreach (var message in remaining) message.Dispose();
            
            _pendingQueue.Dispose();
            _sender.Dispose();
            reorderer.Dispose();
        }
    }
}
