using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using SP.Engine.Runtime;

namespace SP.Engine.Server;

internal class UdpNetworkListener(ListenerInfo listenerInfo) : BaseNetworkListener(listenerInfo)
{
    private readonly object _lock = new();
    private Socket _listenSocket;
    private SocketAsyncEventArgs _receiveEventArgs;
    private byte[] _receiveBuffer;

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

            _receiveBuffer = new byte[ushort.MaxValue];
            eventArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);

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
            
            var segment = new ArraySegment<byte>(e.Buffer!, e.Offset, e.BytesTransferred);
            var remote = (IPEndPoint)e.RemoteEndPoint;
            OnNewClientAccepted(_listenSocket, (segment, remote));
        }
        catch (Exception ex)
        {
            OnError(new Exception($"[UDP] Error receiving data from {e.RemoteEndPoint}: {ex.Message}", ex));
        }

        // 다음 수신 대기
        try
        {
            // 다음 클라이언트의 주소를 받기 위해 EndPoint 초기화
            e.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            
            if (!_listenSocket.ReceiveFromAsync(e))
                OnReceiveCompleted(this, e);
        }
        catch (Exception ex)
        {
            OnError(new Exception(
                $"[UDP] ReceiveFromAsync failed. Remote={e.RemoteEndPoint}, Bytes={e.BytesTransferred}, Error={ex.Message}",
                ex));
        }
    }

    public override void Stop()
    {
        if (_listenSocket == null) return;

        lock (_lock)
        {
            if (_listenSocket == null) return;

            if (_receiveEventArgs != null)
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
