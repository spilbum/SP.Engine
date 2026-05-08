using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using SP.Core;
using SP.Engine.Runtime;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

internal class UdpReceiveEventArgsFactory(int bufferSize) : IPoolObjectFactory<SocketAsyncEventArgs>
{
    private byte[] _globalBuffer;

    public SocketAsyncEventArgs[] Create(int size)
    {
        var totalSize = bufferSize * size;
        _globalBuffer = new byte[totalSize];
        
        var contexts = new SocketAsyncEventArgs[size];
        for (var i = 0; i < size; i++)
        {
            var e = new SocketAsyncEventArgs();
            e.SetBuffer(_globalBuffer, i * bufferSize, bufferSize);
            contexts[i] = e;
        }
        return contexts;
    }
}

internal class UdpNetworkListener(ListenerInfo listenerInfo, IEngineConfig config) : BaseNetworkListener(listenerInfo)
{
    private readonly object _lock = new();
    private Socket _listenSocket;
    private ExpandablePool<SocketAsyncEventArgs> _receiveArgsPool;
    private volatile bool _stopping;

    public override bool Start()
    {
        try
        {
            _listenSocket = new Socket(EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // UDP에서 Port Unreachable (10054) 예외 방지 (Windows 전용)
            if (OperatingSystem.IsWindows())
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                _listenSocket.IOControl(SIO_UDP_CONNRESET, [0], null);
            }
            
            _listenSocket.Bind(EndPoint);

            _receiveArgsPool = new ExpandablePool<SocketAsyncEventArgs>();
            _receiveArgsPool.Initialize(
                config.Session.MaxConnections,
                config.Session.MaxConnections * 2,
                new UdpReceiveEventArgsFactory(config.Network.ReceiveBufferSize));

            var initialLanes = Math.Min(32, config.Session.MaxConnections);
            for (var i = 0; i < initialLanes; i++)
            {
                if (!_receiveArgsPool.TryRent(out var e)) continue;
                e.Completed += OnReceiveCompleted;
                e.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                StartReceive(e);
            }
            
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
        catch (Exception ex)
        {
            HandleNetworkError(ex);
        }
    }
    
    private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        try
        {
            do
            {
                if (e.SocketError == SocketError.Success && e.BytesTransferred > 0)
                {
                    var pooled = new PooledBuffer(e.BytesTransferred);
                    e.Buffer.AsSpan(e.Offset, e.BytesTransferred).CopyTo(pooled.Memory.Span);

                    if (e.RemoteEndPoint is not IPEndPoint ep) break;
                    
                    var remoteEndPoint = new IPEndPoint(ep.Address, ep.Port);
                    OnNewClientAccepted(_listenSocket, (pooled, remoteEndPoint));
                }
                else if (e.SocketError != SocketError.Success)
                {
                    if (!IsIgnoreSocketError(e.SocketError))
                        OnError(new SocketException((int)e.SocketError));

                    if (e.SocketError == SocketError.OperationAborted)
                    {
                        ReturnToReceiveArgsPool(e);
                        break;
                    }
                }

                if (_stopping)
                {
                    ReturnToReceiveArgsPool(e);
                    break;
                }
        
                // 재 사용을 위한 초기화
                e.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                
            } while (!_listenSocket.ReceiveFromAsync(e));
        }
        catch (Exception ex)
        {
            HandleNetworkError(ex);
            ReturnToReceiveArgsPool(e);
        }
    }

    private void ReturnToReceiveArgsPool(SocketAsyncEventArgs e)
    {
        e.Completed -= OnReceiveCompleted;
        _receiveArgsPool.Return(e);
    }

    private void HandleNetworkError(Exception e)
    {
        switch (e)
        {
            case SocketException se when IsIgnoreSocketError(se.SocketErrorCode):
            case ObjectDisposedException or InvalidOperationException:
            case OperationCanceledException:
                    return;
            default:
                OnError(e);
                return;
        }
    }

    private static bool IsIgnoreSocketError(SocketError errorCode)
    {
        return errorCode switch
        {
            SocketError.OperationAborted => true,
            SocketError.Interrupted => true,
            SocketError.NotSocket => true,
            SocketError.ConnectionReset => true,
            _ => false
        };
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
            
            _receiveArgsPool.Dispose();
        }

        OnStopped();
    }
}
