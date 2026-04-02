using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SP.Core;
using SP.Core.Logging;

namespace SP.Engine.Runtime.Networking
{

    public sealed class ReliableSender
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<long, MessageState> _states = new ConcurrentDictionary<long, MessageState>();

        public ReliableSender(ILogger logger)
        {
            _logger = logger;
        }

        public bool Register(TcpMessage message, int initialRtoMs, int maxRetryCount)
        {
            if (message.SequenceNumber == 0) return false;
            
            var state = new MessageState(message, initialRtoMs, maxRetryCount);
            return _states.TryAdd(message.SequenceNumber, state);
        }

        public bool Remove(long sequenceNumber)
        {
            return _states.TryRemove(sequenceNumber, out _);
        }

        public bool TryGetRetryMessages(RtoEstimator rto, out List<TcpMessage> retries)
        {
            retries = null;
            
            var list = new List<TcpMessage>();
            var now = DateTime.UtcNow;
            
            var hasFailure = false;
            
            foreach (var (seq, state) in _states)
            {
                // 만료 안된건 패스
                if (!state.HasExpired(now)) continue;
                
                // 최대 재전송 횟수 도달 체크
                if (state.HasReachedRetryLimit)
                {
                    _states.TryRemove(seq, out _);
                    _logger.Warn("Retry message expired. seq={0}, id={1}", seq, state.Message.Id);

                    hasFailure = true;
                    continue;
                }
                
                if (hasFailure) continue;
                
                var rtoMs = rto.GetRtoMs();
                state.IncrementRetry(rtoMs);
                
                _logger.Debug("Retrying: seq={0}, retry={1}/{2}", seq, state.RetryCount, state.MaxRetryCount);
                list.Add(state.Message);
            }

            if (hasFailure)
            {
                retries = null;
                return false;
            }

            retries = list;
            return true;
        }

        public void Reset() => _states.Clear();

        private class MessageState
        {
            public TcpMessage Message { get; }
            public int MaxRetryCount { get; }
            public int RetryCount { get; private set; }
            public int RtoMs { get; private set; }
            private DateTime _expiresAtUtc;

            public MessageState(TcpMessage message, int initialRtoMs, int maxRetryCount)
            {
                Message = message;
                RtoMs = initialRtoMs;
                MaxRetryCount = maxRetryCount;
                RetryCount = 0;
                UpdateExpiration();
            }
            
            public bool HasExpired(DateTime now) => now >= _expiresAtUtc;
            public bool HasReachedRetryLimit => RetryCount >= MaxRetryCount;

            public void IncrementRetry(int rtoMs)
            {
                RetryCount++;
                RtoMs = rtoMs;
                UpdateExpiration();
            }

            private void UpdateExpiration()
            {
                _expiresAtUtc = DateTime.UtcNow.AddMilliseconds(RtoMs);
            }
        }
    }

    public sealed class RtoEstimator
    {
        private readonly int _minRtoMs;
        private readonly int _maxRtoMs;
        private readonly int _minRtoVarianceMs;
        
        private EwmaFilter _rttVar = new EwmaFilter(0.25);
        private EwmaFilter _smoothedRtt = new EwmaFilter(0.125);

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
            var rto = _smoothedRtt.Value + Math.Max(_minRtoVarianceMs, 4 * _rttVar.Value); // 변동폭 반영
            return (int)Math.Clamp(rto, _minRtoMs, _maxRtoMs);
        }

        public void Reset()
        {
            _smoothedRtt.Reset();
            _rttVar.Reset();
        }
    }

    public sealed class ReliableReceiver
    {
        private const int RecentSeqWindow = 128;
        private readonly object _lock = new object();
        
        // 순서가 어긋난 메시지 보관
        private readonly ConcurrentDictionary<long, TcpMessage> _outOfOrder = new ConcurrentDictionary<long, TcpMessage>();
        
        // 최근 처리한 시퀀스 기록 (중복 패킷 방어)
        private readonly HashSet<long> _recentSeqs = new HashSet<long>();
        private readonly Queue<long> _recentSeqQueue = new Queue<long>();
        private uint _lastProcessedSeq;

        public List<TcpMessage> Process(TcpMessage message)
        {
            var list = new List<TcpMessage>();
            
            // 시퀀스가 없는 경우 (0) 즉시 처리
            if (message.SequenceNumber == 0)
            {
                list.Add(message);
                return list;
            }

            lock (_lock)
            {
                // 중복/오래된 패킷 체크
                if (!TryAcceptSequence(message.SequenceNumber))
                    return list;

                // 정순서 도착
                var nextExpectSeq = _lastProcessedSeq + 1;
                if (message.SequenceNumber == nextExpectSeq)
                {
                    list.AddRange(EmitInOrder(message));
                }
                // 순서 어긋남 -> 버퍼링
                else
                {
                    _outOfOrder.TryAdd(message.SequenceNumber, message);
                }
            }

            return list;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _outOfOrder.Clear();
                _lastProcessedSeq = 0;
                _recentSeqs.Clear();
                _recentSeqQueue.Clear();
            }
        }

        private bool TryAcceptSequence(long sequenceNumber)
        {
            // 이미 처리한 번호는 무시
            if (sequenceNumber <= _lastProcessedSeq) return false;
            
            // 중복 체크
            if (!_recentSeqs.Add(sequenceNumber)) return false;
            
            // 윈도우 크기 유지
            if (_recentSeqQueue.Count >= RecentSeqWindow)
            {
                var old = _recentSeqQueue.Dequeue();
                _recentSeqs.Remove(old);
            }

            _recentSeqQueue.Enqueue(sequenceNumber);
            return true;
        }

        private List<TcpMessage> EmitInOrder(TcpMessage first)
        {
            var list = new List<TcpMessage> { first };
            _lastProcessedSeq = first.SequenceNumber;
            
            var nextSeq = first.SequenceNumber + 1;
            while (_outOfOrder.TryRemove(nextSeq, out var next))
            {
                _lastProcessedSeq = next.SequenceNumber;
                list.Add(next);
            }
            return list;
        }
    }

    public class ReliableMessageProcessor
    {
        private const int PendingBufferWindow = 128;
        
        private readonly SwapQueue<TcpMessage> _pendingQueue;
        private readonly ReliableSender _sender;
        private readonly ReliableReceiver _receiver;
        private readonly RtoEstimator _rto;
        
        private readonly List<TcpMessage> _dequeuedCache = new List<TcpMessage>();
        private int _nextReliableSeq;
        
        public int SendTimeoutMs { get; private set; } = 500;
        public int MaxRetryCount { get; private set; } = 5;
        
        public int PendingCount => _pendingQueue.Count;

        public ReliableMessageProcessor(ILogger logger)
        {
            _pendingQueue = new SwapQueue<TcpMessage>(PendingBufferWindow);
            _sender = new ReliableSender(logger);
            _receiver = new ReliableReceiver();
            _rto = new RtoEstimator();
        }

        public void SetSendTimeoutMs(int ms) => SendTimeoutMs = ms;
        public void SetMaxRetryCount(int count) => MaxRetryCount = count;
        public uint GetNextReliableSeq() => (uint)Interlocked.Increment(ref _nextReliableSeq);
        public bool EnqueuePendingMessage(TcpMessage message) => _pendingQueue.TryEnqueue(message);

        public List<TcpMessage> DequeuePendingMessages()
        {
            _dequeuedCache.Clear();
            _pendingQueue.Exchange(_dequeuedCache);
            return _dequeuedCache.ToList();
        }

        public bool RegisterMessageState(TcpMessage message)
            => _sender.Register(message, SendTimeoutMs, MaxRetryCount);

        public bool RemoveMessageState(long sequenceNumber)
            => _sender.Remove(sequenceNumber);
        
        public bool TryGetRetryMessages(out List<TcpMessage> retries)
            => _sender.TryGetRetryMessages(_rto, out retries);

        public List<TcpMessage> ProcessMessageInOrder(TcpMessage message)
            => _receiver.Process(message);
        
        public void AddRtoSample(double rttMs) => _rto.AddSample(rttMs);

        public void Reset()
        {
            _pendingQueue.Reset();
            _sender.Reset();
            _receiver.Reset();
            _rto.Reset();
            _dequeuedCache.Clear();
            _nextReliableSeq = 0;
        }
    }
}
