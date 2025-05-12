using System;
using System.Net;
using System.Net.Sockets;
using SP.Engine.Runtime;

namespace SP.Engine.Server
{
    internal class UdpSocketListener(ListenerInfo listenerInfo) : BaseSocketListener(listenerInfo)
    {
        private readonly object _lock = new object();
        private Socket _listenSocket;
        private SocketAsyncEventArgs _socketEventArgsReceive;

        public override bool Start()
        {
            try
            {
                _listenSocket = new Socket(EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenSocket.Bind(EndPoint);

                // 원격지 소켓이 닫힌 경우 발생하는 에러를 무시합니다.
                const uint iocIn = 0x80000000;
                const int iocVendor = 0x18000000;
                const uint sioUdpConnReset = iocIn | iocVendor | 12;

                var optionInValue = new[] { Convert.ToByte(false) };
                var optionOutValue = new byte[4];
                _listenSocket.IOControl(unchecked((int)sioUdpConnReset), optionInValue, optionOutValue);

                var eventArgs = new SocketAsyncEventArgs();
                _socketEventArgsReceive = eventArgs;

                eventArgs.Completed += OnReceiveCompleted;
                eventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                const int bufferSize = 1200; // mtu
                var buffer = new byte[bufferSize];
                eventArgs.SetBuffer(buffer, 0, bufferSize);

                _listenSocket.ReceiveFromAsync(eventArgs);
                return true;
            }
            catch (Exception ex)
            {
                OnError(ex);
                return false;
            }          
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                var errorCode = (int)e.SocketError;
                if (errorCode is 995 or 10004 or 10038)
                    return;

                OnError(new SocketException(errorCode));
            }

            if (e.LastOperation != SocketAsyncOperation.ReceiveFrom) 
                return;
            
            try
            {
                if (null == e.RemoteEndPoint)
                    throw new Exception("RemoteEndPoint is null");

                var buffer = e.Buffer.AsSpan(e.Offset, e.BytesTransferred);
                OnNewClientAccepted(_listenSocket, new object[] { buffer.ToArray(), e.RemoteEndPoint });
            }
            catch (Exception ex)
            {
                OnError(new Exception($"[UDP] Error receiving data from {e.RemoteEndPoint}: {ex.Message}", ex));
            }
                
            try
            {
                _listenSocket?.ReceiveFromAsync(e);
            }
            catch (Exception ex)
            {
                OnError(new Exception($"[UDP] Error receiving data from {e.RemoteEndPoint}: {ex.Message}", ex));
            }
        }

        public override void Stop()
        {
            if (null == _listenSocket)
                return;

            lock (_lock)
            {
                if (null == _listenSocket)
                    return;

                if (null != _socketEventArgsReceive)
                {
                    _socketEventArgsReceive.Completed -= OnReceiveCompleted;
                    _socketEventArgsReceive.Dispose();
                    _socketEventArgsReceive = null;
                }

                _listenSocket.SafeClose();             
            }

            OnStopped();
        }
    }
}
