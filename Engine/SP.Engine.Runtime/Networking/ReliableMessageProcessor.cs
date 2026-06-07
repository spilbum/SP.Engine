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
        private void ScheduleNextTimeout(DateTime nowUtc)
        {
            var timeoutUtc = nowUtc.AddMilliseconds(_currentRtoMs);
            Volatile.Write(ref _nextTimeoutTicks, timeoutUtc.Ticks);
        }

        public bool IsExhausted => CurrentAttempt >= _maxAttempts;

        public void RecordRetryAttempt(int nextRtoMs, DateTime nowUtc)
        {
            CurrentAttempt++;
            _currentRtoMs = nextRtoMs;
            ScheduleNextTimeout(nowUtc);
        }
        
        public void Reset(TcpMessage message, int initialRtoMs, int maxAttempts)
        {
            Message = message;
            CurrentAttempt = 0;
            _currentRtoMs = initialRtoMs;
            _maxAttempts = maxAttempts;
            ScheduleNextTimeout(DateTime.UtcNow);
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
        
        private readonly RetransmissionTracker[] _windowSlots;
        private readonly RetransmissionTrackerPool _pool = new RetransmissionTrackerPool();
        private readonly object _lock = new object();
        
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
            _windowSlots = new RetransmissionTracker[windowSize];
            _initialRtoMs = initialRtoMs;
            _maxRetransmitCount = maxRetransmitCount;
        }

        public bool TryPush(TcpMessage message, out TcpMessage inFlightMessage)
        {
            inFlightMessage = null;
            if (Volatile.Read(ref _disposed) == 1) return false;
            
            var seq = (uint)Interlocked.Increment(ref _nextReliableSeq);
            var unaSeq = (uint)Volatile.Read(ref _sendUnaSeq);
            
            if (seq - unaSeq >= _windowSize)
            {
                Interlocked.Decrement(ref _nextReliableSeq);
                Console.WriteLine("seq={0}, unaSeq={1}, windowSize={2}", seq, unaSeq, _windowSize);
                return false;
            }
            
            var index = (int)(seq & _mask);
            var newMessage = message.Extract();
            var tracker = _pool.Rent(newMessage, _initialRtoMs, _maxRetransmitCount);
            newMessage.SetSequenceNumber(seq);

            lock (_lock)
            {
                if (_windowSlots[index] != null)
                {
                    newMessage.Dispose();
                    _pool.Return(tracker);
                    Interlocked.Decrement(ref _nextReliableSeq);
                    Console.WriteLine("_windowSlots[{0}] != null", index);
                    return false;
                }
                
                _windowSlots[index] = tracker;
                
                if (seq > _sendMaxSeq)
                    _sendMaxSeq = seq;
            }
            
            inFlightMessage = newMessage;
            return true;   
        }

        public void Acknowledge(uint remoteAckNumber)
        {
            if (Volatile.Read(ref _disposed) == 1) return;

            var unaSeq = Volatile.Read(ref _sendUnaSeq);
            if ((int)(remoteAckNumber - unaSeq) <= 0) return;

            lock (_lock)
            {
                for (var seq = unaSeq; seq != remoteAckNumber; seq++)
                {
                    var index = (int)(seq & _mask);
                    var tracker = _windowSlots[index];
                    if (tracker == null) continue;
                
                    _windowSlots[index] = null;
                    tracker.Message.Dispose();
                    _pool.Return(tracker); 
                }
                
                Volatile.Write(ref _sendUnaSeq, remoteAckNumber);
            }
        }

        public TcpMessage PrepareRetransmissions(RtoEstimator rtoEstimator, List<TcpMessage> destinationList)
        {
            if (Volatile.Read(ref _disposed) == 1) throw new ObjectDisposedException(nameof(ReliableMessageProcessor));
            
            TcpMessage failed = null;

            var nowUtc = DateTime.UtcNow;

            lock (_lock)
            {
                var unaSeq = _sendUnaSeq;
                var maxSeq = _sendMaxSeq;
                var proceeded = 0;
                const int MAX_RETRANSMIT_PER_TICK = 2;
                
                for (var seq = unaSeq; seq <= maxSeq; seq++)
                {
                    if (proceeded >= MAX_RETRANSMIT_PER_TICK) break;
                    
                    var index = (int)(seq & _mask);
                    var tracker = _windowSlots[index];
                    if (tracker == null) continue;

                    if (!tracker.IsTimeout(nowUtc)) break;

                    if (tracker.IsExhausted)
                    {
                        failed = tracker.Message;
                        break;
                    }
                
                    var rtoMs = rtoEstimator.GetRtoMs(tracker.CurrentAttempt);
                    tracker.RecordRetryAttempt(rtoMs, nowUtc);
                
                    destinationList.Add(tracker.Message.Clone());
                    proceeded++;
                }  
            
                return failed;
            }
        }
        
        public void ResetAll()
        {
            if (Volatile.Read(ref _disposed) == 1) return;

            var currUna = Volatile.Read(ref _sendUnaSeq);
            var currMax = Volatile.Read(ref _sendMaxSeq);
            
            for (var seq = currUna; seq <= currMax; seq++)
            {
                var index = (int)(seq & _mask);
                var tracker = Volatile.Read(ref _windowSlots[index]);
                tracker?.Reset(_initialRtoMs);
            }  
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            
            for (var i = 0; i < _windowSize; i++)
            {
                var tracker = Interlocked.Exchange(ref _windowSlots[i], null);
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

            if (double.IsNaN(baseRto) || double.IsInfinity(baseRto) || baseRto <= 0)
            {
                baseRto = _minRtoMs;
            }
            
            var safeRetryCount = Math.Min(retryCount, 10);
            var backoffRto = baseRto * (1 << safeRetryCount);
            
            return (int)Math.Clamp(backoffRto, _minRtoMs, _maxRtoMs);
        }
    }

    public enum ReceiveIngestResult
    {
        Success,
        Buffered,
        Duplicate,
        BufferOverflow,
    }
    
    public sealed class ReceiveSequenceReorderer : IDisposable
    {
        private readonly ReaderWriterLockSlim _rwlock = new ReaderWriterLockSlim();
        private readonly int _maxOutOfOrderCount;
        private uint _nextExpectedSeq = 1;
        private readonly SortedDictionary<uint, TcpMessage> _outOfOrderSlots = new SortedDictionary<uint, TcpMessage>();
        private bool _disposed;
        
        public uint NextExpectedSeq
        {
            get
            {
                _rwlock.EnterReadLock();
                try
                {
                    return _nextExpectedSeq;
                }
                finally
                {
                    _rwlock.ExitReadLock();
                }
            }
        }

        public ReceiveSequenceReorderer(int maxOutOfOrderCount)
        {
            _maxOutOfOrderCount = maxOutOfOrderCount;
        }

        public ReceiveIngestResult TryIngest(TcpMessage message, List<TcpMessage> destinationList)
        {
            if (_disposed) return ReceiveIngestResult.Success;
            
            _rwlock.EnterWriteLock();
            try
            {
                var seq = message.SequenceNumber;
                if (seq < _nextExpectedSeq || _outOfOrderSlots.ContainsKey(seq))
                {
                    return ReceiveIngestResult.Duplicate;
                }

                if (seq == _nextExpectedSeq)
                {
                    destinationList.Add(message.Extract());
                    _nextExpectedSeq++;

                    while (_outOfOrderSlots.Remove(_nextExpectedSeq, out var nextMessage))
                    {
                        destinationList.Add(nextMessage);
                        _nextExpectedSeq++;
                    }
                    
                    return ReceiveIngestResult.Success;
                }

                if (_outOfOrderSlots.Count >= _maxOutOfOrderCount)
                {
                    return ReceiveIngestResult.BufferOverflow;
                }
                
                _outOfOrderSlots.TryAdd(seq, message.Extract());
                return ReceiveIngestResult.Buffered;
            }
            finally
            {
                _rwlock.ExitWriteLock();
            }
        }
        
        public void Dispose()
        {
            _rwlock.EnterWriteLock();
            try
            {
                if (_disposed) return;
                _disposed = true;
                foreach (var message in _outOfOrderSlots.Values) message.Dispose();
                _outOfOrderSlots.Clear();   
            }
            finally
            {
                _rwlock.ExitWriteLock();
            }
            _rwlock.Dispose();
        }
    }
    
    public class ReliableMessageProcessor : IDisposable
    {
        private readonly ReliableSendWindow _reliableSendWindow;
        private readonly ReceiveSequenceReorderer _receiveSequenceReorderer;
        private readonly RtoEstimator _rtoEstimator = new RtoEstimator();
        private readonly List<TcpMessage> _dequeuedCache = new List<TcpMessage>();
        private readonly SwapQueue<TcpMessage> _pendingQueue;
        
        public int MaxAckDelayMs { get; private set; }
        public int AckFrequency { get; private set; }
        public uint NextExpectedSeq => _receiveSequenceReorderer.NextExpectedSeq;

        private ReliableMessageProcessor(Builder builder)
        {
            _reliableSendWindow = new ReliableSendWindow(
                builder.InFlightLimit,
                builder.InitialRetransmitTimeoutMs,
                builder.MaxRetransmitCount);
            _pendingQueue = new SwapQueue<TcpMessage>(builder.PendingQueueCapacity);
            _receiveSequenceReorderer = new ReceiveSequenceReorderer(builder.MaxOutOfOrderCount);
            
            MaxAckDelayMs = builder.MaxAckDelayMs;
            AckFrequency = builder.AckFrequency;
        }

        public static Builder CreateBuilder() => new Builder();

        public bool EnqueuePendingMessage(TcpMessage message)
        {
            var tcp = message.Extract();
            if (_pendingQueue.TryEnqueue(tcp)) return true;
            tcp.Dispose();
            return false;
        }

        public List<TcpMessage> FlushPendingMessages()
        {
            _dequeuedCache.Clear();
            _pendingQueue.Extract(_dequeuedCache);
            return _dequeuedCache;
        }

        public bool RegisterInFlight(TcpMessage message, out TcpMessage inFlightMessage)
            => _reliableSendWindow.TryPush(message, out inFlightMessage);

        public void AcknowledgeInFlight(uint remoteAckNumber)
            => _reliableSendWindow.Acknowledge(remoteAckNumber);

        public TcpMessage PrepareRetransmissions(List<TcpMessage> destinationList)
            => _reliableSendWindow.PrepareRetransmissions(_rtoEstimator, destinationList);
        
        public ReceiveIngestResult ReceiveIngestMessage(TcpMessage message, List<TcpMessage> destinationList)
            => _receiveSequenceReorderer.TryIngest(message, destinationList);

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
            _receiveSequenceReorderer.Dispose();
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
