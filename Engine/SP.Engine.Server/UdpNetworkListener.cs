using System;
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
    private volatile bool _stopping;

    public override bool Start()
    {
        try
        {
            _stopping = false;
            _listenSocket = new Socket(EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // UDP에서 Port Unreachable (10054) 예외 방지 (Windows 전용)
            if (OperatingSystem.IsWindows())
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                _listenSocket.IOControl(SIO_UDP_CONNRESET, [0], null);
            }
            
            _listenSocket.Bind(EndPoint);

            _receiveEventArgs = new SocketAsyncEventArgs();
            _receiveEventArgs.Completed += OnReceiveCompleted;
            _receiveEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            _receiveBuffer = new byte[ushort.MaxValue];
            _receiveEventArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            
            StartReceive(_receiveEventArgs);
            return true;
        }
        catch (Exception ex)
        {
            OnError(ex);
            return false;
        }
    }

    private void StartReceive(SocketAsyncEventArgs e)
    {
        try
        {
            if (_stopping || _listenSocket == null) return;

            if (!_listenSocket.ReceiveFromAsync(e))
            {
                OnReceiveCompleted(this, e);
            }
        }
        catch (ObjectDisposedException) {}
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        var pending = false;
        while (!pending)
        {
            if (_stopping) return;
            
            if (e.SocketError != SocketError.Success)
            {
                HandleSocketError(e);
                return;
            }

            if (e.BytesTransferred > 0 && e.RemoteEndPoint != null)
            {
                try
                {
                    var segment = new ArraySegment<byte>(e.Buffer!, e.Offset, e.BytesTransferred);
                    var remote = (IPEndPoint)e.RemoteEndPoint;
                    
                    OnNewClientAccepted(_listenSocket, (segment, remote));
                }
                catch (Exception ex)
                {
                    OnError(new Exception($"[UDP] Packet processing error: {ex.Message}", ex));
                }
            }
        
            // 다음 수신을 위한 초기화
            e.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            
            try
            {
                if (_stopping) break;
                // 다시 수신 시도
                pending = _listenSocket.ReceiveFromAsync(e);
            }
            catch (ObjectDisposedException)
            {
                pending = true;
            }
            catch (Exception ex)
            {
                OnError(ex);
                pending = true;
            }
        }
    }

    private void HandleSocketError(SocketAsyncEventArgs e)
    {
        var errorCode = (int)e.SocketError;
        // 995: Operation Aborted (소켓 닫힘), 10004: Interrupted, 10038: Not a socket
        if (!_stopping && errorCode is not (995 or 10004 or 10038))
        {
            OnError(new SocketException(errorCode));
        }
    }

    public override void Stop()
    {
        lock (_lock)
        {
            if (_stopping) return;
            _stopping = true;

            if (_listenSocket != null)
            {
                _listenSocket.SafeClose();
                _listenSocket = null;
            }
            
            if (_receiveEventArgs != null)
            {
                var tempArgs = _receiveEventArgs;
                _receiveEventArgs = null;
                
                tempArgs.Completed -= OnReceiveCompleted;
                tempArgs.Dispose();
            }
        }

        OnStopped();
    }
}
