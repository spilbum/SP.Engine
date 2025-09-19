using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server
{
    public interface ITcpNetworkSession : ILogContext
    {
        SocketReceiveContext ReceiveContext { get; }
        void ProcessReceive(SocketAsyncEventArgs e);
    }

    public class TcpNetworkSession(Socket client, SocketReceiveContext socketReceiveContext) : BaseNetworkSession(ESocketMode.Tcp, client), ITcpNetworkSession
    {
        private SocketAsyncEventArgs _sendEventArgs;        

        ILogger ILogContext.Logger => ClientSession.Logger;
        public SocketReceiveContext ReceiveContext { get; } = socketReceiveContext;

        public override void Attach(IClientSession session)
        {
            base.Attach(session);
            ReceiveContext.Initialize(this);
            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.Completed += OnSendCompleted;
        }

        public override void Start()
        {
            StartReceive(ReceiveContext.SocketEventArgs);
        }

        protected override void OnClosed(ECloseReason reason)
        {
            var e = _sendEventArgs;
            if (null == e)            
            {
                base.OnClosed(reason);
                return;
            }

            // 전송 이벤트 초기화
            if (Interlocked.CompareExchange(ref _sendEventArgs, null, e) != e) 
                return;
            
            e.Dispose();
            base.OnClosed(reason);
        }

        public bool TrySend(TcpMessage message)
            => TrySend(message.Payload);

        private void StartReceive(SocketAsyncEventArgs e)
        {
            bool isRaiseEvent;

            try
            {
                var offset = ReceiveContext.OriginOffset;
                if (e.Offset != offset)
                    e.SetBuffer(offset, Config.Network.ReceiveBufferSize);

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
                ClientSession.ProcessBuffer(e.Buffer, e.Offset, e.BytesTransferred);
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

        protected override void Send(SegmentQueue queue)
        {
            try
            {
                if (null == _sendEventArgs)
                    throw new InvalidOperationException("_socketEventArgsSend is null");
                
                _sendEventArgs.UserToken = queue;

                if (1 < queue.Count)
                    _sendEventArgs.BufferList = queue;
                else
                {
                    var data = queue[0];
                    _sendEventArgs.SetBuffer(data.Array, data.Offset, data.Count);
                }

                var socket = Client;
                if (null == socket)
                {
                    OnSendError(queue, ECloseReason.SocketError);
                    return;
                }
                
                if (!socket.SendAsync(_sendEventArgs))
                    OnSendCompleted(socket, _sendEventArgs);
            }
            catch (Exception ex)
            {
                LogError(ex);

                ClearPrevSendState(_sendEventArgs);
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
            if (e.UserToken is not SegmentQueue queue)
            {
                ClientSession.Logger.Error("SendingQueue is null");
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
                queue.TrimSentBytes(e.BytesTransferred);
                ClientSession.Logger.Warn("{0} of {1} were transferred, send the rest {2} bytes right now.", e.BytesTransferred, count, queue.Sum(x => x.Count));
                ClearPrevSendState(e);
                Send(queue);
                return;
            }

            ClearPrevSendState(e);
            OnSendCompleted(queue);
        }
    }
}
