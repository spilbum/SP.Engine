using System;
using System.Net.Sockets;

namespace SP.Engine.Server;

public sealed class SocketReceiveContext : IDisposable
{
    public SocketAsyncEventArgs SocketEventArgs { get; }
    public int OriginOffset { get; }

    public SocketReceiveContext(SocketAsyncEventArgs e)
    {
        SocketEventArgs = e;
        OriginOffset = e.Offset;
        SocketEventArgs.Completed += OnReceiveCompleted;
    }
    
    public void Initialize(TcpNetworkSession ns)
    {
        SocketEventArgs.UserToken = ns;
    }
    
    private static void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (e.UserToken is not TcpNetworkSession session) return;
        if (e.LastOperation != SocketAsyncOperation.Receive) return;
        var state = (session, e);
        session.AsyncRun(state, static s => s.session.ProcessReceive(s.e));
    }

    public void Reset()
    {
        SocketEventArgs.UserToken = null;
    }

    public void Dispose()
    {
        SocketEventArgs.Completed -= OnReceiveCompleted;
        SocketEventArgs.Dispose();
    }
}
