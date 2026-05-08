using System;
using System.Net.Sockets;

namespace SP.Engine.Server;

public sealed class SocketReceiveContext : IDisposable
{
    private bool _disposed;
    
    public SocketAsyncEventArgs SocketEventArgs { get; }
    public int OriginOffset { get; }

    public SocketReceiveContext(SocketAsyncEventArgs e)
    {
        SocketEventArgs = e;
        OriginOffset = e.Offset;
        SocketEventArgs.Completed += OnReceiveCompleted;
    }
    
    public void Initialize(ITcpNetworkSession tcpSession)
    {
        SocketEventArgs.UserToken = tcpSession;
    }
    
    private static void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (e.UserToken is not TcpNetworkSession ns) return;
        if (e.LastOperation == SocketAsyncOperation.Receive)
        {
            ns.AsyncRun(() => ns.ProcessReceive(e));
        }
    }

    public void Reset()
    {
        SocketEventArgs.UserToken = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        SocketEventArgs.Completed -= OnReceiveCompleted;
        SocketEventArgs.Dispose();
        _disposed = true;
    }
}
