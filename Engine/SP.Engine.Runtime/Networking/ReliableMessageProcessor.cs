using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace SP.Engine.Runtime.Networking
{
    public sealed class AdaptiveRttEstimator
    {
        private const double Alpha = 0.125;
        private const double Beta = 0.25;
        private const int K = 4;
        
        private bool _initialized;
        private double _srtt;
        private double _rttvar;

        private readonly int _minTimeoutMs = 200;
        private readonly int _maxTimeoutMs = 5000;

        public void OnSample(double rttMs)
        {
            if (!_initialized)
            {
                _srtt = rttMs;
                _rttvar = rttMs / 2;
                _initialized = true;
                return;
            }

            var diff = Math.Abs(_srtt - rttMs);
            _rttvar = (1 - Beta) * _rttvar + Beta * diff;
            _srtt = (1 - Alpha) * _srtt + Alpha * rttMs;
        }

        public int GetTimeoutMs()
        {
            var rto = _srtt + K * _rttvar;
            return (int)Math.Clamp(rto, _minTimeoutMs, _maxTimeoutMs);
        }

        public void Reset()
        {
            _initialized = false;
            _srtt = 0;
            _rttvar = 0;
        }
    }
    
    public abstract class ReliableMessageProcessor
    {
        private sealed class SendingState
        {
            public IMessage Message { get; }
            public int MaxReSendCnt { get; }
            public int ReSendCnt { get; private set; }
            public DateTime ExpireTime { get; private set; }
            public int TimeoutMs { get; private set; }
            
            public SendingState(IMessage message, int initialTimeoutMs, int maxReSendCnt)
            {
                Message = message;
                MaxReSendCnt = maxReSendCnt;
                TimeoutMs = initialTimeoutMs;
                ReSendCnt = 0;
                RefreshExpire();
            }
            
            public void IncrementReSend(int timeoutMs)
            {
                ReSendCnt++;
                TimeoutMs = timeoutMs;
                RefreshExpire();
            }
            
            public bool HasExpired => DateTime.UtcNow >= ExpireTime;
            private void RefreshExpire() => ExpireTime = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
        }
        
        private readonly ConcurrentQueue<IMessage> _pendingSendQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentDictionary<long, SendingState> _sendingWindow = new ConcurrentDictionary<long, SendingState>();
        private readonly ConcurrentDictionary<long, IMessage> _receivingWindow = new ConcurrentDictionary<long, IMessage>();
        private readonly AdaptiveRttEstimator _rttEstimator = new AdaptiveRttEstimator();
        private readonly HashSet<long> _deliveredSequences = new HashSet<long>();
        private readonly object _receiveLock = new object();

        private long _nextSequenceNumber;
        private long _expectedReceiveSequenceNumber;
        
        public int InitialSendTimeoutMs { get; private set; } = 500;
        public int MaxReSendCnt { get; private set; } = 5;
        
        public void SetInitialSendTimeoutMs(int ms) => InitialSendTimeoutMs = ms;
        public void SetMaxReSendCnt(int cnt) => MaxReSendCnt = cnt;
        protected long GetNextSequenceNumber() => Interlocked.Increment(ref _nextSequenceNumber);
        
        protected void EnqueuePendingSend(IMessage message) => _pendingSendQueue.Enqueue(message);
        protected IEnumerable<IMessage> DequeuePendingSend()
        {
            while (_pendingSendQueue.TryDequeue(out var message))
                yield return message;
        }

        protected bool StartSendingMessage(IMessage message)
        {
            var state = new SendingState(message, InitialSendTimeoutMs, MaxReSendCnt);
            return _sendingWindow.TryAdd(message.SequenceNumber, state);
        }

        protected void OnAckReceived(long sequenceNumber)
        {
            _sendingWindow.TryRemove(sequenceNumber, out _);
        }

        protected void RecordRttSample(double rttMs)
        {
            _rttEstimator.OnSample(rttMs);
        }

        protected IEnumerable<IMessage> GetResendCandidates()
        {
            foreach (var kvp in _sendingWindow)
            {
                var state = kvp.Value;
                if (!state.HasExpired)
                    continue;

                if (state.ReSendCnt >= state.MaxReSendCnt)
                    yield return null;
                else
                {
                    state.IncrementReSend(_rttEstimator.GetTimeoutMs());
                    yield return state.Message;
                }
            }
        }

        protected IEnumerable<IMessage> ProcessReceivedMessage(IMessage message)
        {
            lock (_receiveLock)
            {
                if (message.SequenceNumber < _expectedReceiveSequenceNumber ||
                    _deliveredSequences.Contains(message.SequenceNumber))
                    yield break;

                if (message.SequenceNumber == _expectedReceiveSequenceNumber)
                {
                    yield return message;
                    _deliveredSequences.Add(message.SequenceNumber);
                    _expectedReceiveSequenceNumber++;

                    while (_receivingWindow.TryRemove(_expectedReceiveSequenceNumber, out var received))
                    {
                        yield return received;
                        _deliveredSequences.Add(received.SequenceNumber);
                        _expectedReceiveSequenceNumber++;
                    }
                }
                else
                {
                    _receivingWindow.TryAdd(message.SequenceNumber, message);
                }
            }
        }

        protected void ResetProcessorState()
        {
            _rttEstimator.Reset();
            _sendingWindow.Clear();
            _pendingSendQueue.Clear();

            lock (_receiveLock)
            {
                _receivingWindow.Clear();
                _deliveredSequences.Clear();
                _expectedReceiveSequenceNumber = 0;
            }
        }
    }
}
