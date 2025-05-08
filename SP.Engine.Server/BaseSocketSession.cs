
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Utilities;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server
{
    [Flags]
    public enum ESocketState
    {
        InSending = 1 << 0,
        InReceiving = 1 << 1,
        InClosing = 1 << 4,
        Closed = 1 << 24
    }

    public enum ESocketMode
    {
        Tcp = 0,
        Udp = 1
    }

    public interface ISocketSession
    { 
        ESocketMode Mode { get; }
        string SessionId { get; }
        Socket Client { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        ISession Session { get; }
        IEngineConfig Config { get; }
        event Action<ISocketSession, ECloseReason> Closed;
        void Initialize(ISession session);
        void Close(ECloseReason reason);
        bool TrySend(ArraySegment<byte> data);
    }

    internal abstract class BaseSocketSession(ESocketMode mode) : ISocketSession
    {
        // 1st byte : Closed (y/n)
        // 2nd byte : N/A
        // 3rd byte : CloseReason
        // last byte : Normal State
        private volatile int _state;
        private readonly object _lock = new object();
        private Socket _client;
        private ISmartPool<SendingQueue> _sendingQueuePool;
        private SendingQueue _sendingQueue;
        private ISession _session;

        public Socket Client => _client;
        public ESocketMode Mode { get; private set; } = mode;
        public ISession Session => _session ?? throw new InvalidOperationException("Session is not initialized.");
        public IEngineConfig Config { get; private set; }
        public string SessionId { get; }
        public IPEndPoint LocalEndPoint { get; protected set; }
        public IPEndPoint RemoteEndPoint { get; protected set; }

        private Action<ISocketSession, ECloseReason> _closed;
        public event Action<ISocketSession, ECloseReason> Closed
        {
            add => _closed += value;
            remove => _closed -= value;
        }

        protected bool IsInClosingOrClosed => _state >= (int)ESocketState.InClosing;
        protected bool IsClosed => _state >= (int)ESocketState.Closed;

        protected BaseSocketSession(ESocketMode mode, Socket client)
            : this (mode)
        {
            _client = client 
                ?? throw new ArgumentNullException(nameof(client));

            LocalEndPoint = client.LocalEndPoint as IPEndPoint;
            RemoteEndPoint = client.RemoteEndPoint as IPEndPoint;
            SessionId = Guid.NewGuid().ToString();
        }        

        public virtual void Initialize(ISession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Config = session.Config;

            if (((ISocketServerAccessor)session.Engine).SocketServer is SocketServer socketServer)
                _sendingQueuePool = socketServer.SendingQueuePool ?? throw new ArgumentException("SendingQueuePool is null");

            if (!_sendingQueuePool.Rent(out var queue) || queue == null)
            {
                throw new InvalidOperationException("Failed to acquire a SendingQueue from the pool.");
            }

            _sendingQueue = queue;
            queue.StartEnqueue();
        }

        public abstract void Start();

        protected virtual void OnClosed(ECloseReason reason)
        {
            // 종료 플래그 설정
            if (!TryAddState(ESocketState.Closed))
                return;

            // 전송 큐 반환
            var queue = Interlocked.Exchange(ref _sendingQueue, null);
            if (queue != null)
            {
                queue.Clear();
                _sendingQueuePool?.Return(queue);
            }

            // 종료 이벤트 호출
            lock (_lock)
            {
                _closed?.Invoke(this, reason);
            }
        }

        public bool TrySend(ArraySegment<byte> data)
        {
            if (IsClosed)
                return false;

            var queue = _sendingQueue;
            if (queue == null || !queue.Enqueue(data, queue.TrackId))
                return false;

            StartSend(queue, queue.TrackId, true);
            return true;
        }

        protected abstract void Send(SendingQueue queue);

        private void StartSend(SendingQueue queue, int trackId, bool isInit)
        {
            if (isInit)
            {
                if (!TryAddState(ESocketState.InSending))
                    return;

                var currentQueue = _sendingQueue;
                if (queue != currentQueue || trackId != currentQueue.TrackId)
                {                   
                    OnSendEnd();
                    return;
                }
            }

            if (IsInClosingOrClosed && TryValidateClosedBySocket(out _))
            {
                OnSendEnd(true);
                return;
            }            

            if (!_sendingQueuePool.Rent(out var newQueue))
            {
                Session.Logger.Error("Unable to acquire a new sending queue from the pool.");
                OnSendEnd(false);
                Close(ECloseReason.InternalError);
                return;
            }

            var oldQueue = Interlocked.CompareExchange(ref _sendingQueue, newQueue, queue);
            if (!ReferenceEquals(oldQueue, queue))
            {
                if (null != newQueue)
                    _sendingQueuePool.Return(newQueue);

                if (IsInClosingOrClosed)
                    OnSendEnd(true);
                else
                {
                    OnSendEnd(false);
                    Session.Logger.Error("Failed to switch the sending queue.");
                    Close(ECloseReason.InternalError);
                }

                return;
            }

            queue.StopEnqueue();
            newQueue?.StartEnqueue();

            if (0 == queue.Count)
            {
                Session.Logger.Error("There is no data to be sent in the queue.");
                _sendingQueuePool.Return(queue);
                OnSendEnd(false);
                Close(ECloseReason.InternalError);
                return;
            }

            Send(queue);
        }

        private void OnSendEnd() => OnSendEnd(IsInClosingOrClosed);

        private void OnSendEnd(bool isInClosingOrClosed)
        {
            RemoveState(ESocketState.InSending);

            if (!isInClosingOrClosed)
                return;
            
            if (!TryValidateClosedBySocket(out var client))
            {
                var queue = _sendingQueue;
                if (queue != null && queue.Count > 0) return;
                if (null != client)
                    InternalClose(client, GetCloseReasonFromSocketState(), false);
                else
                    OnClosed(GetCloseReasonFromSocketState());

                return;
            }                

            if (IsIdle())
            {
                OnClosed(GetCloseReasonFromSocketState());
            }
        }

        protected void OnSendCompleted(SendingQueue queue)
        {
            queue.Clear();
            _sendingQueuePool.Return(queue);

            var newQueue = _sendingQueue;
            if (IsInClosingOrClosed)
            {
                if (newQueue != null && newQueue.Count == 0 && !TryValidateClosedBySocket(out var _))
                {
                    StartSend(newQueue, newQueue.TrackId, false);
                    return;
                }

                OnSendEnd(true);
                return;
            }

            if (null == newQueue || 0 == newQueue.Count)
                OnSendEnd();
            else
                StartSend(newQueue, newQueue.TrackId, false);
        }

        public void Close(ECloseReason reason)
        {
            if (!TryAddState(ESocketState.InClosing))
                return;

            if (TryValidateClosedBySocket(out var client))
                return;

            if (CheckState(ESocketState.InSending))
            {
                TryAddState(GetSocketState(reason), false);
                return;
            }

            if (client != null)
                InternalClose(client, reason, true);
            else
                OnClosed(reason);
        }

        private void InternalClose(Socket client, ECloseReason reason, bool isSetCloseReason)
        {
            if (Interlocked.CompareExchange(ref _client, null, client) != client)
                return;
            
            if (isSetCloseReason)
                TryAddState(GetSocketState(reason), false);

            client.SafeClose();

            if (IsIdle())
            {
                OnClosed(reason);
            }
        }
        
        private static ESocketState GetSocketState(ECloseReason reason)
        {
            return (ESocketState)(((int)reason & 0xFF) << 16);
        }  

        protected virtual bool TryValidateClosedBySocket(out Socket client)
        {
            client = _client;
            return null == client;
        }

        protected virtual bool IsIgnoreSocketError(int socketErrorCode)
        {
            return socketErrorCode == 10004 || socketErrorCode == 10053 || socketErrorCode == 10054 ||
                   socketErrorCode == 10058 || socketErrorCode == 10060 || socketErrorCode == 995;
        }

        protected virtual bool IsIgnoreException(Exception e, out int errorCode)
        {
            errorCode = 0;
            switch (e)
            {
                case ObjectDisposedException _:
                case NullReferenceException _:
                    return true;
                case SocketException socketEx:
                    errorCode = socketEx.ErrorCode;
                    return IsIgnoreSocketError(errorCode);
                default:
                    return false;
            }
        }

        private bool TryAddState(ESocketState state, bool ensureNotClosing)
        {
            while (true)
            {
                var oldState = _state;
                if (ensureNotClosing && oldState >= (int)ESocketState.InClosing)
                    return false;

                var newState = oldState | (int)state;
                if (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState) 
                    continue;
                
                return true;
            }
        }

        private bool TryAddState(ESocketState state)
        {
            while (true)
            {
                var oldState = _state;
                var newState = _state | (int)state;

                //Already marked
                if (oldState == newState)
                {
                    return false;
                }

                var compareState = Interlocked.CompareExchange(ref _state, newState, oldState);
                if (compareState == oldState)
                    return true;
            }
        }

        private void RemoveState(ESocketState state)
        {
            while (true)
            {
                var oldState = _state;
                var newState = oldState & ~(int)state;
                if (Interlocked.CompareExchange(ref _state, newState, oldState) == oldState)
                    break;
                
            }
        }

        private bool CheckState(ESocketState state)
        {
            return (_state & (int)state) != 0;
        }

        private bool IsIdle()
        {
            return (_state & ((int)ESocketState.InSending | (int)ESocketState.InReceiving)) == 0;
        }

        private ECloseReason GetCloseReasonFromSocketState()
        {
            return (ECloseReason)((_state >> 16) & 0xFF);
        }

        private void ValidateClosed(ECloseReason closeReason)
        {
            if (IsClosed)
                return;
    
            // 종료 중인지 체크
            if (CheckState(ESocketState.InClosing))
            {
                if (IsIdle())
                {
                    // 송수신 중이 아니면 종료
                    OnClosed(GetCloseReasonFromSocketState());
                }
            }
            else
            {
                Close(closeReason);
            }
        }

        protected void OnSendError(SendingQueue queue, ECloseReason closeReason)
        {
            queue.Clear();
            _sendingQueuePool.Return(queue);
            OnSendEnd(IsInClosingOrClosed);
            ValidateClosed(closeReason);
        }

        protected void OnReceiveTerminated()
        {
            OnReceiveTerminated(GetCloseReasonFromSocketState());
        }

        protected void OnReceiveTerminated(ECloseReason reason)
        {
            OnReceiveEnded();
            ValidateClosed(reason);
        }

        protected bool OnReceiveStarted()
        {
            return TryAddState(ESocketState.InReceiving, true);
        }

        protected void OnReceiveEnded()
        {
            RemoveState(ESocketState.InReceiving);
        }

        private const string ErrorMessageFormat = "An error occurred: {0}\r\n{1}";
        private const string SocketErrorFormat = "An socket error occurred: {0}";
        private const string CallerFormat = "caller={0}, file path={1}, line number={2}";

        protected void LogError(Exception e, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = -1)
        {
            if (IsIgnoreException(e, out var socketErrorCode))
                return;

            var message = 0 < socketErrorCode ? string.Format(SocketErrorFormat, socketErrorCode) : string.Format(ErrorMessageFormat, e.Message, e.StackTrace);
            Session.Logger.Error(message + Environment.NewLine + string.Format(CallerFormat, caller, filePath, lineNumber));
        }

        protected void LogError(int socketErrorCode, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = -1)
        {
            if (!Config.IsLogAllSocketError && IsIgnoreSocketError(socketErrorCode))
                return;

            Session.Logger.Error(string.Format(SocketErrorFormat, socketErrorCode) + Environment.NewLine + string.Format(CallerFormat, caller, filePath, lineNumber));
        }
    }
}
