using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SP.Core;
using SP.Core.Logging;

namespace SP.Engine.Runtime.Networking
{

    public sealed class ReliableSender
    {
        private readonly object _lock = new object();
        private readonly SortedDictionary<uint, MessageState> _states = new SortedDictionary<uint, MessageState>();
        
        public bool Register(TcpMessage message, int initialRtoMs, int maxRetryCount)
        {
            if (message.SequenceNumber == 0) return false;

            lock (_lock)
            {
                var state = new MessageState(message, initialRtoMs, maxRetryCount);
                return _states.TryAdd(message.SequenceNumber, state);   
            }
        }

        public void RemoveUntil(uint ackNumber)
        {
            lock (_lock)
            {
                var keysToRemove = _states
                    .Keys
                    .TakeWhile(seq => seq <= ackNumber)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _states.Remove(key);
                }
            }
        }

        public (List<TcpMessage> Retries, List<TcpMessage> Failed) UpdateAndExtract(RtoEstimator rto)
        {
            var now = DateTime.UtcNow;
            var retries = new List<TcpMessage>();
            var failed = new List<TcpMessage>();

            lock (_lock)
            {
                foreach (var (seq, state) in _states)
                {
                    // 만료 안된건 패스
                    if (!state.HasExpired(now)) continue;
                
                    // 최대 재전송 횟수 도달 체크
                    if (state.HasReachedRetryLimit)
                    {
                        failed.Add(state.Message);
                        continue;
                    }
                
                    var nextRto = rto.GetRtoMs(state.RetryCount);
                    state.IncrementRetry(nextRto);
                    retries.Add(state.Message);
                }
                
                foreach (var msg in failed)
                {
                    _states.Remove(msg.SequenceNumber);
                }
            }
            
            return (retries, failed);
        }

        public void Reset()
        {
            lock (_lock)
            {
                _states.Clear();
            }
        }

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

        public int GetRtoMs(int retryCount)
        {
            var rto = _smoothedRtt.Value + Math.Max(_minRtoVarianceMs, 4 * _rttVar.Value); // 변동폭 반영
            rto *= Math.Pow(2, retryCount); 
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
        private readonly object _lock = new object();
        private readonly Dictionary<long, TcpMessage> _outOfOrder = new Dictionary<long, TcpMessage>();
        
        public uint LastProcessedSequence { get; private set; }

        public List<TcpMessage> Process(TcpMessage message)
        {
            // 시퀀스가 없는 경우 (0) 즉시 처리
            if (message.SequenceNumber == 0) return null;

            lock (_lock)
            {
                // 중복/오래된 패킷 체크
                if (message.SequenceNumber <= LastProcessedSequence)
                    return null;

                if (_outOfOrder.ContainsKey(message.SequenceNumber))
                    return null;

                // 정순서 도착
                if (message.SequenceNumber == LastProcessedSequence + 1)
                {
                    return EmitInOrder(message);
                }
                
                // 순서 어긋남 -> 버퍼링
                _outOfOrder.TryAdd(message.SequenceNumber, message);
                return null;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _outOfOrder.Clear();
                LastProcessedSequence = 0;
            }
        }

        private List<TcpMessage> EmitInOrder(TcpMessage first)
        {
            var list = new List<TcpMessage> { first };
            LastProcessedSequence = first.SequenceNumber;
            
            while (_outOfOrder.Remove(LastProcessedSequence + 1, out var next))
            {
                LastProcessedSequence = next.SequenceNumber;
                list.Add(next);
            }
            return list;
        }
    }

    public class ReliableMessageProcessor
    {
        private const int PendingBufferWindow = 128;
        
        private readonly SwapQueue<TcpMessage> _pendingQueue = new SwapQueue<TcpMessage>(PendingBufferWindow);
        private readonly ReliableSender _sender = new ReliableSender();
        private readonly ReliableReceiver _receiver = new ReliableReceiver();
        private readonly RtoEstimator _rtoEstimator = new RtoEstimator();
        
        private readonly List<TcpMessage> _dequeuedCache = new List<TcpMessage>();
        private int _nextReliableSeq;
        
        public int SendTimeoutMs { get; private set; } = 500;
        public int MaxRetryCount { get; private set; } = 5;
        public int MaxAckDelayMs { get; private set; } = 30;
        public int AckStepThreshold { get; private set; } = 10;
        public uint LastSequenceNumber => _receiver.LastProcessedSequence;
        public int PendingCount => _pendingQueue.Count;

        public void SetSendTimeoutMs(int ms) => SendTimeoutMs = ms;
        public void SetMaxRetryCount(int count) => MaxRetryCount = count;
        public void SetMaxAckDelayMs(int ms) => MaxAckDelayMs = ms;
        public void SetAckStepThreshold(int count) => AckStepThreshold = count;
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

        public void RemoveMessageStates(uint ackNumber)
            => _sender.RemoveUntil(ackNumber);
        
        public (List<TcpMessage> Retries, List<TcpMessage> Failed) ExtractRetryMessages()
            => _sender.UpdateAndExtract(_rtoEstimator);

        public List<TcpMessage> ProcessMessageInOrder(TcpMessage message)
            => _receiver.Process(message);
        
        public void AddRtoSample(double rttMs) => _rtoEstimator.AddSample(rttMs);

        public void Reset()
        {
            _pendingQueue.Reset();
            _sender.Reset();
            _receiver.Reset();
            _rtoEstimator.Reset();
            _dequeuedCache.Clear();
            _nextReliableSeq = 0;
        }
    }
}
