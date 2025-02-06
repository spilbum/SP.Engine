using System;
using System.Net.Sockets;

namespace SP.Engine.Server
{
    internal class TcpAsyncSocketListener : SocketListenerBase
    {
        private readonly int _backLog;
        private Socket _socket;
        private SocketAsyncEventArgs _socketEventArgsAccept;
        private bool _disposed;
        
        public TcpAsyncSocketListener(ListenerInfo info)
            : base(info)
        {
            _backLog = info.BackLog;
        }

        public override bool Start()
        {
            if (_disposed)
                return false;
            
            _socket = new Socket(EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            
            try
            {
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(false, 0));
                
                _socket.Bind(EndPoint);
                _socket.Listen(_backLog);

                _socketEventArgsAccept = new SocketAsyncEventArgs();
                _socketEventArgsAccept.Completed += AcceptCompleted;

                if (!_socket.AcceptAsync(_socketEventArgsAccept))
                    ProcessAccept(_socketEventArgsAccept);

                return true;
            }
            catch (SocketException e)
            {
                OnError(new Exception($"SocketException: {e.Message}, ErrorCode: {e.ErrorCode}"));
                return false;
            }
            catch (Exception e)
            {
                OnError(e);
                return false;
            }
        }

        private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (_disposed)
                return;
            
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError == SocketError.Success)
                {
                    var socket = e.AcceptSocket;
                    if (null == socket)
                        throw new SocketException((int)SocketError.SocketError);
                    
                    OnNewClientAccepted(socket, null);
                }
                else
                {
                    throw new SocketException((int)e.SocketError);
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                e.AcceptSocket = null;
                if (!_disposed && !_socket.AcceptAsync(e))
                    ProcessAccept(e);
            }
        }

        public override void Stop()
        {
            if (_disposed)
                return;

            try
            {
                _socket?.Close();
            }
            finally
            {
                Dispose();
            }
        }

        protected override void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            _socketEventArgsAccept?.Dispose();
            _socket?.Dispose();
            
            base.Dispose();
        }
    }
}
