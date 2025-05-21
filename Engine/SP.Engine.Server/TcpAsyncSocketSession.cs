using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime;

namespace SP.Engine.Server
{
    internal interface ITcpAsyncSocketSession : ILogContext
    {
        SocketAsyncEventArgsProxy ReceiveSocketEventArgsProxy { get; }
        void ProcessReceive(SocketAsyncEventArgs e);
    }

    internal class TcpAsyncSocketSession(Socket client, SocketAsyncEventArgsProxy socketEventArgsProxy) : BaseSocketSession(ESocketMode.Tcp, client), ITcpAsyncSocketSession
    {
        private SocketAsyncEventArgs _socketEventArgsSend;        

        ILogger ILogContext.Logger => Session.Logger;
        public SocketAsyncEventArgsProxy ReceiveSocketEventArgsProxy { get; } = socketEventArgsProxy;

        public override void Initialize(ISession session)
        {
            base.Initialize(session);
            ReceiveSocketEventArgsProxy.Initialize(this);
            _socketEventArgsSend = new SocketAsyncEventArgs();
            _socketEventArgsSend.Completed += OnSendCompleted;
        }

        public override void Start()
        {
            StartReceive(ReceiveSocketEventArgsProxy.SocketEventArgs);
        }

        protected override void OnClosed(ECloseReason reason)
        {
            var e = _socketEventArgsSend;
            if (null == e)            
            {
                base.OnClosed(reason);
                return;
            }

            // 전송 이벤트 초기화
            if (Interlocked.CompareExchange(ref _socketEventArgsSend, null, e) != e) 
                return;
            
            e.Dispose();
            base.OnClosed(reason);
        }

        private void StartReceive(SocketAsyncEventArgs e)
        {
            bool isRaiseEvent;

            try
            {
                var offset = ReceiveSocketEventArgsProxy.OriginOffset;
                if (e.Offset != offset)
                    e.SetBuffer(offset, Config.ReceiveBufferSize);

                if (!OnReceiveStarted())
                {
                    if (IsInClosingOrClosed)
                        OnReceiveTerminated();

                    return;
                }
                
                isRaiseEvent = Client.ReceiveAsync(e);
            }
            catch (Exception ex)
            {
                LogError(ex);
                OnReceiveTerminated(ECloseReason.SocketError);
                return;
            }

            if (!isRaiseEvent)
                ProcessReceive(e);
        }

        public void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (!ProcessCompleted(e))
            {
                // e.SocketError == Socket.Success and e.BytesTransferred == 0 : close packet
                OnReceiveTerminated(e.SocketError == SocketError.Success ? ECloseReason.ClientClosing : ECloseReason.SocketError);
                return;
            }

            OnReceiveEnded();

            try
            {
                Session.ProcessBuffer(e.Buffer, e.Offset, e.BytesTransferred);
            }
            catch (Exception ex)
            {
                LogError(ex);
                Close(ECloseReason.ProtocolError);
                return;
            }

            StartReceive(e);
        }


        private bool ProcessCompleted(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                if (0 < e.BytesTransferred)
                    return true;
            }
            else
            {
                LogError((int)e.SocketError);
            }             

            return false;
        }

        protected override void Send(SendingQueue queue)
        {
            try
            {
                if (null == _socketEventArgsSend)
                    throw new InvalidOperationException("_socketEventArgsSend is null");
                
                _socketEventArgsSend.UserToken = queue;

                if (1 < queue.Count)
                    _socketEventArgsSend.BufferList = queue;
                else
                {
                    var data = queue[0];
                    _socketEventArgsSend.SetBuffer(data.Array, data.Offset, data.Count);
                }

                var socket = Client;
                if (null == socket)
                {
                    OnSendError(queue, ECloseReason.SocketError);
                    return;
                }
                
                if (!socket.SendAsync(_socketEventArgsSend))
                    OnSendCompleted(socket, _socketEventArgsSend);
            }
            catch (Exception ex)
            {
                LogError(ex);

                ClearPrevSendState(_socketEventArgsSend);
                OnSendError(queue, ECloseReason.SocketError);
            }
        }
        
        private static void ClearPrevSendState(SocketAsyncEventArgs e)
        {
            if (null == e)
                return;

            e.UserToken = null;

            if (e.Buffer != null)
                e.SetBuffer(null, 0, 0);
            else if (e.BufferList != null)
                e.BufferList = null;
        }


        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken is not SendingQueue queue)
            {
                Session.Logger.Error("SendingQueue is null");
                return;
            }

            if (!ProcessCompleted(e))
            {
                ClearPrevSendState(e);
                OnSendError(queue, ECloseReason.SocketError);
                return;
            }

            var count = queue.Sum(x => x.Count);
            if (count != e.BytesTransferred)
            {
                queue.InternalTrim(e.BytesTransferred);
                Session.Logger.Warn("{0} of {1} were transferred, send the rest {2} bytes right now.", e.BytesTransferred, count, queue.Sum(x => x.Count));
                ClearPrevSendState(e);
                Send(queue);
                return;
            }

            ClearPrevSendState(e);
            OnSendCompleted(queue);
        }
    }
}
