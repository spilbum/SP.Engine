using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server
{
    public interface ITcpNetworkSession : ILogContext, IReliableSender
    {
        SocketReceiveContext ReceiveContext { get; }
        void ProcessReceive(SocketAsyncEventArgs e);
    }

    public class TcpNetworkSession(Socket client, SocketReceiveContext socketReceiveContext) : BaseNetworkSession(SocketMode.Tcp, client), ITcpNetworkSession, IReliableSender
    {
        private SocketAsyncEventArgs _sendEventArgs;        

        ILogger ILogContext.Logger => Session.Logger;
        public SocketReceiveContext ReceiveContext { get; } = socketReceiveContext;

        public override void Attach(IBaseSession session)
        {
            base.Attach(session);
            ReceiveContext.Initialize(this);
            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.Completed += OnSendCompleted;
        }

        public void Start()
        {
            StartReceive(ReceiveContext.SocketEventArgs);
        }

        protected override void OnClosed(CloseReason reason)
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
            => TrySend(message.ToArraySegment());

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
                OnReceiveTerminated(CloseReason.SocketError);
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
                OnReceiveTerminated(e.SocketError == SocketError.Success ? CloseReason.ClientClosing : CloseReason.SocketError);
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
                Close(CloseReason.ProtocolError);
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
                    OnSendError(queue, CloseReason.SocketError);
                    return;
                }
                
                if (!socket.SendAsync(_sendEventArgs))
                    OnSendCompleted(socket, _sendEventArgs);
            }
            catch (Exception ex)
            {
                LogError(ex);

                ClearPrevSendState(_sendEventArgs);
                OnSendError(queue, CloseReason.SocketError);
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
                Session.Logger.Error("SendingQueue is null");
                return;
            }

            if (!ProcessCompleted(e))
            {
                ClearPrevSendState(e);
                OnSendError(queue, CloseReason.SocketError);
                return;
            }

            var count = queue.Sum(x => x.Count);
            if (count != e.BytesTransferred)
            {
                queue.TrimSentBytes(e.BytesTransferred);
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
