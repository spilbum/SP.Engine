using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using SP.Engine.Runtime;

namespace SP.Engine.Server
{
    internal class UdpNetworkListener(ListenerInfo listenerInfo) : BaseNetworkListener(listenerInfo)
    {
        private readonly object _lock = new();
        private Socket _listenSocket;
        private SocketAsyncEventArgs _receiveEventArgs;

        public override bool Start()
        {
            try
            {
                _listenSocket = new Socket(EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenSocket.Bind(EndPoint);

                if (OperatingSystem.IsWindows())
                {
                    const uint iocIn = 0x80000000;
                    const int iocVendor = 0x18000000;
                    const uint sioUdpConnReset = iocIn | iocVendor | 12;

                    var optionInValue = new[] { Convert.ToByte(false) };
                    var optionOutValue = new byte[4];
                    _listenSocket.IOControl(unchecked((int)sioUdpConnReset), optionInValue, optionOutValue);
                }

                var eventArgs = new SocketAsyncEventArgs();
                _receiveEventArgs = eventArgs;

                eventArgs.Completed += OnReceiveCompleted;
                eventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                var buffer = new byte[ushort.MaxValue];
                eventArgs.SetBuffer(buffer, 0, buffer.Length);

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
                if (errorCode is not (995 or 10004 or 10038))
                    OnError(new SocketException(errorCode));
                return;
            }

            if (e.LastOperation != SocketAsyncOperation.ReceiveFrom || e.BytesTransferred == 0) 
                return;
            
            try
            {
                if (null == e.RemoteEndPoint)
                    throw new Exception("RemoteEndPoint is null");

                var datagram = new byte[e.BytesTransferred];
                Buffer.BlockCopy(e.Buffer!, e.Offset, datagram, 0, e.BytesTransferred);
                
                var remote = (IPEndPoint)e.RemoteEndPoint;
                remote = new IPEndPoint(remote.Address, remote.Port);
                
                OnNewClientAccepted(_listenSocket, (datagram, remote));
            }
            catch (Exception ex)
            {
                OnError(new Exception($"[UDP] Error receiving data from {e.RemoteEndPoint}: {ex.Message}", ex));
            }
            
            try
            {
                e.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                if (!_listenSocket.ReceiveFromAsync(e))
                    OnReceiveCompleted(this, e);
            }
            catch (Exception ex)
            {
                OnError(new Exception("[UDP] ReceiveFromAsync failed. Remote={e.RemoteEndPoint}, Bytes={e.BytesTransferred}, Error={ex.Message}", ex));
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

                if (null != _receiveEventArgs)
                {
                    _receiveEventArgs.Completed -= OnReceiveCompleted;
                    _receiveEventArgs.Dispose();
                    _receiveEventArgs = null;
                }

                _listenSocket.SafeClose();             
            }

            OnStopped();
        }
    }
}
