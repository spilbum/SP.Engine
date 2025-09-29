using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using SP.Common;

namespace SP.Engine.Runtime.Networking
{
    public abstract class ReliableMessageProcessor
    {
        private sealed class SendingMessageState
        {
            public IMessage Message { get; }
            public int MaxReSendCnt { get; }
            public int ReSendCnt { get; private set; }
            public DateTime ExpireTime { get; private set; }
            public int TimeoutMs { get; private set; }
            public int InitTimeoutMs { get; }

            public SendingMessageState(IMessage message, int initialTimeoutMs, int maxReSendCnt)
            {
                Message = message;
                MaxReSendCnt = maxReSendCnt;
                TimeoutMs = initialTimeoutMs;
                ReSendCnt = 0;
                InitTimeoutMs = initialTimeoutMs;
                RefreshExpire();
            }
            
            public void IncrementReSend(int timeoutMs)
            {
                ReSendCnt++;
                TimeoutMs = timeoutMs;
                RefreshExpire();
            }

            public void Reset()
            {
                ReSendCnt = 0;
                TimeoutMs = InitTimeoutMs;
                RefreshExpire();
            }
            
            public bool HasExpired => DateTime.UtcNow >= ExpireTime;
            private void RefreshExpire() => ExpireTime = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
        }

        private sealed class RtoCalculator
        {
            private readonly EwmaFilter _srtt = new EwmaFilter(0.125);
            private readonly EwmaFilter _rttVar = new EwmaFilter(0.25);
            
            private const int G = 10;
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

            public int GetTimeoutMs()
            {
                var rto = _srtt.Value + Math.Max(G, 4 * _rttVar.Value);
                return (int)Math.Clamp(rto, MinRtoMs, MaxRtoMs);
            }

            public void Reset()
            {
                _srtt.Reset();
                _rttVar.Reset();
            }
        }
        
        private readonly ConcurrentQueue<IMessage> _pendingMessageSendQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentDictionary<long, SendingMessageState> _sendingMessageStateDict = new ConcurrentDictionary<long, SendingMessageState>();
        private readonly ConcurrentDictionary<long, IMessage> _receivedMessageDict = new ConcurrentDictionary<long, IMessage>();
        private readonly RtoCalculator _rtoCalc = new RtoCalculator();
        private readonly object _receiveLock = new object();

        private long _nextSequenceNumber;
        private long _expectedReceiveSequenceNumber = 1;
        
        private readonly HashSet<long> _receivedSeqSet = new HashSet<long>();
        private readonly Queue<long> _receivedSeqQueue = new Queue<long>();
        
        public int SendTimeoutMs { get; private set; } = 500;
        public int MaxReSendCnt { get; private set; } = 5;

        private bool VerifySequence(long seq)
        {
            if (seq < _expectedReceiveSequenceNumber)
                return false;
            
            if (_receivedSeqSet.Contains(seq))
                return false;

            if (_receivedSeqQueue.Count >= 128) 
                _receivedSeqSet.Remove(_receivedSeqQueue.Dequeue());
            
            _receivedSeqQueue.Enqueue(seq);
            _receivedSeqSet.Add(seq);
            return true;
        }

        private IEnumerable<IMessage> YieldInOrder(IMessage startingMessage)
        {
            yield return startingMessage;
            _expectedReceiveSequenceNumber++;

            while (_receivedMessageDict.TryRemove(_expectedReceiveSequenceNumber, out var nextMessage))
            {
                yield return nextMessage;
                _expectedReceiveSequenceNumber++;
            }
        }
        
        public void SetSendTimeoutMs(int ms) => SendTimeoutMs = ms;
        public void SetMaxResendCnt(int cnt) => MaxReSendCnt = cnt;
        protected long GetNextSequenceNumber() => Interlocked.Increment(ref _nextSequenceNumber);
        protected void EnqueuePendingSend(IMessage message) => _pendingMessageSendQueue.Enqueue(message);
        protected IEnumerable<IMessage> DequeuePendingSend()
        {
            while (_pendingMessageSendQueue.TryDequeue(out var message))
                yield return message;
        }

        protected bool StartSendingMessage(IMessage message)
        {
            if (message.SequenceNumber <= 0) return false;   
            var state = new SendingMessageState(message, SendTimeoutMs, MaxReSendCnt);
            return _sendingMessageStateDict.TryAdd(message.SequenceNumber, state);
        }

        protected void OnAckReceived(long sequenceNumber)
        {
            _sendingMessageStateDict.TryRemove(sequenceNumber, out _);
        }

        protected void RecordRttSample(double rawRtt)
        {
            _rtoCalc.AddSample(rawRtt);
        }

        protected IEnumerable<IMessage> GetResendCandidates()
        {
            foreach (var (key, state) in _sendingMessageStateDict)
            {
                if (!state.HasExpired)
                    continue;

                if (state.ReSendCnt >= state.MaxReSendCnt)
                {
                    _sendingMessageStateDict.TryRemove(key, out _);
                    OnExceededResendCnt(state.Message);
                    continue;
                }
                
                state.IncrementReSend(_rtoCalc.GetTimeoutMs());
                
                OnDebug($"Resending message: seq={key}, count={state.ReSendCnt}, expireTime={state.ExpireTime:HH:mm:ss.fff}, timeoutMs={state.TimeoutMs}");
                yield return state.Message;
            }
        }

        protected IEnumerable<IMessage> ProcessReceivedMessage(IMessage message)
        {
            if (message.SequenceNumber == 0)
            {
                yield return message;
                yield break;
            }
            
            lock (_receiveLock)
            {
                if (!VerifySequence(message.SequenceNumber))
                {
                    yield break;
                }

                if (message.SequenceNumber == _expectedReceiveSequenceNumber)
                {
                    foreach (var next in YieldInOrder(message))
                        yield return next;
                }
                else
                {
                    _receivedMessageDict.TryAdd(message.SequenceNumber, message);
                }
            }
        }

        protected void ResetSendingMessageState()
        {
            foreach (var kvp in _sendingMessageStateDict)
                kvp.Value.Reset();
        }

        protected void ResetMessageProcessor()
        {
            _rtoCalc.Reset();
            _sendingMessageStateDict.Clear();
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
