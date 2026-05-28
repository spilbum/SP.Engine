using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using SP.Core;
using SP.Core.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public class RetransmissionTrackerPool
    {
        private readonly ConcurrentStack<RetransmissionTracker> _pool = new ConcurrentStack<RetransmissionTracker>();

        public RetransmissionTracker Rent(TcpMessage message, int initialRtoMs, int maxAttempts)
        {
            if (!_pool.TryPop(out var tracker))
                return new RetransmissionTracker(message, initialRtoMs, maxAttempts);
                    
            tracker.Reset(message, initialRtoMs, maxAttempts);
            return tracker;   
        }

        public void Return(RetransmissionTracker tracker)
        {
            tracker.Clear();
            _pool.Push(tracker);   
        }

        public void Clear()
        {
            _pool.Clear();
        }
    }
    
    public class RetransmissionTracker
    {
        private int _maxAttempts;
        private int _currentRtoMs;
        private long _nextTimeoutTicks;

        public TcpMessage Message { get; private set; }
        public int CurrentAttempt { get; private set; }

        public RetransmissionTracker(TcpMessage message, int initialRtoMs, int maxAttempts)
        {
            Reset(message, initialRtoMs, maxAttempts);
        }

        public void Reset(int initialRtoMs)
        {
            Reset(Message, initialRtoMs, _maxAttempts);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTimeout(DateTime nowUtc)
            => nowUtc.Ticks >= Volatile.Read(ref _nextTimeoutTicks);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ScheduleNextTimeout()
        {
            var timeoutUtc = DateTime.UtcNow.AddMilliseconds(_currentRtoMs);
            Volatile.Write(ref _nextTimeoutTicks, timeoutUtc.Ticks);
        }

        public bool IsExhausted => CurrentAttempt >= _maxAttempts;

        public void RecordRetryAttempt(int nextRtoMs)
        {
            CurrentAttempt++;
            _currentRtoMs = nextRtoMs;
            ScheduleNextTimeout();
        }
        
        public void Reset(TcpMessage message, int initialRtoMs, int maxAttempts)
        {
            Message = message;
            CurrentAttempt = 0;
            _currentRtoMs = initialRtoMs;
            _maxAttempts = maxAttempts;
            ScheduleNextTimeout();
        }

        public void Clear()
        {
            Message = null;
        }
    }

    public sealed class ReliableSendWindow : IDisposable
    {
        private readonly int _windowSize;
        private readonly int _mask;
        private readonly int _initialRtoMs;
        private readonly int _maxRetransmitCount;
        
        private readonly RetransmissionTracker[] _slots;
        private readonly RetransmissionTrackerPool _pool = new RetransmissionTrackerPool();

        private readonly object _windowLock = new object();
        
        /// <summary>
        /// Ack를 받지 못한 가장 오래된 시퀀스 번호
        /// </summary>
        private long _sendUnaSeq = 1;
        /// <summary>
        /// 현재까지 발송된 가장 높은 시퀀스 번호
        /// </summary>
        private long _sendMaxSeq;
        /// <summary>
        /// 발급 대기 중인 시퀀스 번호
        /// </summary>
        private long _nextReliableSeq;
        private int _disposed;

        public ReliableSendWindow(int windowSize, int initialRtoMs, int maxRetransmitCount)
        {
            _windowSize = windowSize;
            _mask = windowSize - 1;
            _slots = new RetransmissionTracker[windowSize];
            _initialRtoMs = initialRtoMs;
            _maxRetransmitCount = maxRetransmitCount;
        }

        public bool Register(TcpMessage message)
        {
            if (Volatile.Read(ref _disposed) == 1) return false;

            var seq = (uint)Interlocked.Increment(ref _nextReliableSeq);
            var unaSeq = (uint)Volatile.Read(ref _sendUnaSeq);
            
            if (seq - unaSeq >= _windowSize)
            {
                Interlocked.Decrement(ref _nextReliableSeq);
                return false;
            }

            var index = (int)(seq & _mask);
            var tracker = _pool.Rent(message, _initialRtoMs, _maxRetransmitCount);

            lock (_windowLock)
            {
                if (_slots[index] != null)
                {
                    _pool.Return(tracker);
                    Interlocked.Decrement(ref _nextReliableSeq);
                    return false;
                }
                
                _slots[index] = tracker;
                message.SetSequenceNumber(seq);
                message.Retain();

                var maxSeq = _sendMaxSeq;
                if (seq > maxSeq) _sendMaxSeq = seq;
            }
            
            return true;   
        }

        public void RemoveUntil(uint remoteAckNumber)
        {
            if (Volatile.Read(ref _disposed) == 1) return;

            lock (_windowLock)
            {
                var unaSeq = _sendUnaSeq;
                if ((int)(remoteAckNumber - unaSeq) <= 0) return;
            
                for (var seq = unaSeq; seq != remoteAckNumber; seq++)
                {
                    var index = (int)(seq & _mask);
                    var tracker = _slots[index];
                    if (tracker == null) continue;
                    
                    _slots[index] = null;
                    tracker.Message.Dispose();
                    _pool.Return(tracker); 
                }
                
                _sendUnaSeq = remoteAckNumber;   
            }
        }

        public TcpMessage ProcessRetransmissions(RtoEstimator rtoEstimator, List<TcpMessage> destinationList)
        {
            if (Volatile.Read(ref _disposed) == 1) throw new ObjectDisposedException(nameof(ReliableMessageProcessor));
            
            var nowUtc = DateTime.UtcNow;
            TcpMessage failed = null;

            lock (_windowLock)
            {
                var unaSeq = _sendUnaSeq;
                var maxSeq = _sendMaxSeq;
                
                for (var seq = unaSeq; seq <= maxSeq; seq++)
                {
                    var index = (int)(seq & _mask);
                    var tracker = _slots[index];
                    if (tracker == null) continue;

                    if (!tracker.IsTimeout(nowUtc)) 
                        break;

                    if (tracker.IsExhausted)
                    {
                        failed = tracker.Message;
                        break;
                    }
                
                    // 재전송 대상
                    tracker.RecordRetryAttempt(rtoEstimator.GetRtoMs(tracker.CurrentAttempt));
                    destinationList.Add(tracker.Message);
                }      
            }
            
            return failed;
        }
        
        public void ResetAll()
        {
            if (Volatile.Read(ref _disposed) == 1) return;

            lock (_windowLock)
            {
                var currUna = _sendUnaSeq;
                var currMax = _sendMaxSeq;
            
                for (var seq = currUna; seq <= currMax; seq++)
                {
                    var index = (int)(seq & _mask);
                    _slots[index]?.Reset(_initialRtoMs);
                }   
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

            for (var i = 0; i < _windowSize; i++)
            {
                var tracker = Interlocked.Exchange(ref _slots[i], null);
                tracker?.Message.Dispose();
            }
                
            _pool.Clear();
            _nextReliableSeq = 0;
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

    public sealed class SequenceBuffer : IDisposable
    {
        private readonly object _lock = new object();
        private readonly int _maxBufferSize;
        private uint _nextExpectedSeq = 1;
        private readonly SortedDictionary<uint, TcpMessage> _buffer = new SortedDictionary<uint, TcpMessage>();
        private bool _disposed;
        
        public uint NextExpectedSeq
        {
            get
            {
                lock (_lock) return _nextExpectedSeq;
            }
        }

        public SequenceBuffer(int maxBufferSize)
        {
            _maxBufferSize = maxBufferSize;
        }

        public List<TcpMessage> Push(TcpMessage message)
        {
            if (_disposed) return null;
            
            lock (_lock)
            {
                var seq = message.SequenceNumber;
                if (seq < _nextExpectedSeq || _buffer.ContainsKey(seq))
                    return null;

                if (seq == _nextExpectedSeq)
                {
                    var readyList = new List<TcpMessage> { message };
                    message.Retain();
                    _nextExpectedSeq++;

                    while (_buffer.Remove(_nextExpectedSeq, out var nextMessage))
                    {
                        readyList.Add(nextMessage);
                        _nextExpectedSeq++;
                    }
                    
                    return readyList;
                }

                if (_buffer.TryAdd(message.SequenceNumber, message))
                    message.Retain();

                return null;
            }
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
        private readonly ReliableSendWindow _reliableSendWindow;
        private readonly SequenceBuffer _sequenceBuffer;
        private readonly RtoEstimator _rtoEstimator = new RtoEstimator();
        private readonly List<TcpMessage> _dequeuedCache = new List<TcpMessage>();
        private readonly SwapQueue<TcpMessage> _pendingQueue;
        
        public int MaxAckDelayMs { get; private set; }
        public int AckFrequency { get; private set; }
        public uint NextExpectedSeq => _sequenceBuffer.NextExpectedSeq;

        private ReliableMessageProcessor(Builder builder)
        {
            _reliableSendWindow = new ReliableSendWindow(
                builder.InFlightLimit,
                builder.InitialRetransmitTimeoutMs,
                builder.MaxRetransmitCount);
            _pendingQueue = new SwapQueue<TcpMessage>(builder.PendingQueueCapacity);
            _sequenceBuffer = new SequenceBuffer(builder.MaxOutOfOrderCount);
            
            MaxAckDelayMs = builder.MaxAckDelayMs;
            AckFrequency = builder.AckFrequency;
        }

        public static Builder CreateBuilder() => new Builder();

        public void EnqueuePendingMessage(TcpMessage message)
        {
            if (_pendingQueue.TryEnqueue(message))
                message.Retain();
            else
                message.Dispose();
        }

        public List<TcpMessage> FlushPendingMessages()
        {
            _dequeuedCache.Clear();
            _pendingQueue.Extract(_dequeuedCache);
            return _dequeuedCache;
        }

        public bool PrepareReliableSend(TcpMessage message)
            => _reliableSendWindow.Register(message);

        public void AcknowledgeInFlight(uint remoteAckNumber)
            => _reliableSendWindow.RemoveUntil(remoteAckNumber);

        public TcpMessage ExtractRetransmissions(List<TcpMessage> destinationList)
            => _reliableSendWindow.ProcessRetransmissions(_rtoEstimator, destinationList);
        
        public List<TcpMessage> IngestReceivedMessage(TcpMessage message)
            => _sequenceBuffer.Push(message);

        public void ResetInFlightMessages()
            => _reliableSendWindow.ResetAll();
        
        public void AddRtoSample(double rttMs) => _rtoEstimator.AddSample(rttMs);

        public void Dispose()
        {
            var remaining = new List<TcpMessage>();
            _pendingQueue.Extract(remaining);
            foreach (var message in remaining) message.Dispose();
            
            _pendingQueue.Dispose();
            _reliableSendWindow.Dispose();
            _sequenceBuffer.Dispose();
        }

        public sealed class Builder
        {
            internal int InFlightLimit { get; private set; } = 2048;
            internal int PendingQueueCapacity { get; private set; } = 1024;
            internal int MaxOutOfOrderCount { get; private set; } = 100;
            internal int MaxRetransmitCount { get; private set; } = 3;
            internal int InitialRetransmitTimeoutMs { get; private set; } = 500;
            internal int MaxAckDelayMs { get; private set; } = 30;
            internal int AckFrequency { get; private set; } = 10;
            
            internal Builder() {}

            public Builder SetInFlightLimit(int limit)
            {
                if (limit <= 0 || (limit & limit - 1) != 0)
                {
                    throw new ArgumentException("InFlightLimit must be a power of 2.");
                }
                
                InFlightLimit = limit;
                return this;
            }

            public Builder SetPendingQueueCapacity(int capacity)
            {
                if (capacity <= 0) throw new ArgumentException("Capacity must be greater than zero.");
                PendingQueueCapacity = capacity;
                return this;
            }

            public Builder SetRetransmitPolicy(int maxCount, int timeoutMs)
            {
                MaxRetransmitCount = maxCount > 0 ? maxCount : 3;
                InitialRetransmitTimeoutMs = timeoutMs > 0 ? timeoutMs : 500;
                return this;
            }

            public Builder SetAckPolicy(int delayMs, int frequency)
            {
                MaxAckDelayMs = delayMs > 0 ? delayMs : 30;
                AckFrequency = frequency > 0 ? frequency : 10;
                return this;
            }

            public Builder SetMaxOutOfOrderCount(int count)
            {
                MaxOutOfOrderCount = count > 0 ? count : 100;
                return this;
            }

            public ReliableMessageProcessor Build()
            {
                return new ReliableMessageProcessor(this);
            }
        }
    }
}
