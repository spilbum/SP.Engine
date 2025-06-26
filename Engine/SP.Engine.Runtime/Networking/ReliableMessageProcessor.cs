using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace SP.Engine.Runtime.Networking
{
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
        
        private sealed class RtoCalculator
        {
            private const int MarginMs = 100;
            private const int MinRtoMs = 200;
            private const int MaxRtoMs = 5000;
        
            private readonly double _alpha;
            private bool _initialized;

            public double SrttMs { get; private set; }
        
            public RtoCalculator(double alpha = 0.125)
            {
                if (alpha < 0.0 || alpha > 1.0)
                    throw new ArgumentOutOfRangeException(nameof(alpha), "alpha must be between 0.0 and 1.0");
                _alpha = alpha;
            }
        
            public void OnSample(double rawRtt)
            {
                if (!_initialized)
                {
                    SrttMs = rawRtt;
                    _initialized = true;
                    return;
                }

                SrttMs = (1 - _alpha) * SrttMs + _alpha * rawRtt;
            }

            public int GetTimeoutMs()
            {
                var rto = SrttMs * 2.0 + MarginMs;
                return (int)Math.Clamp(rto, MinRtoMs, MaxRtoMs);
            }

            public void Reset()
            {
                SrttMs = 0;
                _initialized = false;
            }
        }
        
        private readonly ConcurrentQueue<IMessage> _pendingMessageSendQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentDictionary<long, SendingState> _sendingMessageDict = new ConcurrentDictionary<long, SendingState>();
        private readonly ConcurrentDictionary<long, IMessage> _receivedMessageDict = new ConcurrentDictionary<long, IMessage>();
        private readonly RtoCalculator _rtoCalc = new RtoCalculator();
        private readonly object _receiveLock = new object();

        private long _nextSequenceNumber;
        private long _expectedReceiveSequenceNumber = 1;
        
        private readonly HashSet<long> _receivedSeqSet = new HashSet<long>();
        private readonly Queue<long> _receivedSeqQueue = new Queue<long>();
        
        public int InitialSendTimeoutMs { get; private set; } = 500;
        public int MaxReSendCnt { get; private set; } = 5;

        private bool IsDuplicate(long seq)
        {
            if (_receivedSeqSet.Contains(seq))
                return true;

            if (_receivedSeqQueue.Count >= 128) 
                _receivedSeqSet.Remove(_receivedSeqQueue.Dequeue());
            
            _receivedSeqQueue.Enqueue(seq);
            _receivedSeqSet.Add(seq);
            return false;
        }
        
        public void SetInitialSendTimeoutMs(int ms) => InitialSendTimeoutMs = ms;
        public void SetMaxReSendCnt(int cnt) => MaxReSendCnt = cnt;
        protected long GetNextSequenceNumber() => Interlocked.Increment(ref _nextSequenceNumber);
        protected void EnqueuePendingSend(IMessage message) => _pendingMessageSendQueue.Enqueue(message);
        protected IEnumerable<IMessage> DequeuePendingSend()
        {
            while (_pendingMessageSendQueue.TryDequeue(out var message))
                yield return message;
        }

        protected bool StartSendingMessage(IMessage message)
        {
            var state = new SendingState(message, InitialSendTimeoutMs, MaxReSendCnt);
            return _sendingMessageDict.TryAdd(message.SequenceNumber, state);
        }

        protected void OnAckReceived(long sequenceNumber)
        {
            _sendingMessageDict.TryRemove(sequenceNumber, out _);
        }

        protected void RecordRttSample(double rawRtt)
        {
            _rtoCalc.OnSample(rawRtt);
        }

        protected IEnumerable<IMessage> GetResendCandidates()
        {
            foreach (var (key, state) in _sendingMessageDict)
            {
                if (!state.HasExpired)
                    continue;

                if (state.ReSendCnt >= state.MaxReSendCnt)
                {
                    _sendingMessageDict.TryRemove(key, out _);
                    OnExceededResendCnt(state.Message);
                    continue;
                }

                var timeoutMs = _rtoCalc.GetTimeoutMs();
                state.IncrementReSend(timeoutMs);
                OnDebug("Message resend: seq={0}, timeoutMs={1}", state.Message.SequenceNumber, timeoutMs);
                yield return state.Message;
            }
        }

        protected IEnumerable<IMessage> ProcessReceivedMessage(IMessage message)
        {
            lock (_receiveLock)
            {
                if (message.SequenceNumber < _expectedReceiveSequenceNumber || IsDuplicate(message.SequenceNumber))
                    yield break;

                if (message.SequenceNumber == _expectedReceiveSequenceNumber)
                {
                    yield return message;
                    _expectedReceiveSequenceNumber++;

                    while (_receivedMessageDict.TryRemove(_expectedReceiveSequenceNumber, out var received))
                    {
                        yield return received;
                        _expectedReceiveSequenceNumber++;
                    }
                }
                else
                {
                    _receivedMessageDict.TryAdd(message.SequenceNumber, message);
                }
            }
        }

        protected void ResetProcessorState()
        {
            _rtoCalc.Reset();
            _sendingMessageDict.Clear();
            _pendingMessageSendQueue.Clear();

            lock (_receiveLock)
            {
                _receivedMessageDict.Clear();
                _expectedReceiveSequenceNumber = 0;
                _receivedSeqQueue.Clear();
                _receivedSeqSet.Clear();
            }
        }

        protected abstract void OnDebug(string format, params object[] args);
        protected abstract void OnExceededResendCnt(IMessage message);
    }
}
