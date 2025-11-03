using System;
using System.Net.Sockets;

namespace SP.Engine.Server;

public class SocketReceiveContext
{
    public SocketReceiveContext(SocketAsyncEventArgs e)
    {
        SocketEventArgs = e;
        OriginOffset = e.Offset;
        SocketEventArgs.Completed += OnReceiveCompleted;
    }

    public SocketAsyncEventArgs SocketEventArgs { get; }
    public int OriginOffset { get; private set; }

    private static void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (e.UserToken is not ITcpNetworkSession networkSession)
            return;

        if (e.LastOperation == SocketAsyncOperation.Receive)
            networkSession.AsyncRun(() => networkSession.ProcessReceive(e));
        else
            throw new ArgumentException("The last operation completed on the socket was not a receive");
    }

    public void Initialize(ITcpNetworkSession networkSession)
    {
        SocketEventArgs.UserToken = networkSession;
    }

    public void Reset()
    {
        SocketEventArgs.UserToken = null;
    }
}
