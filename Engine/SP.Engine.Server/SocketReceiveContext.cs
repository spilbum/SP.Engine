using System;
using System.Buffers;
using System.Net.Sockets;

namespace SP.Engine.Server;

public sealed class SocketReceiveContext : IDisposable
{
    private bool _disposed;
    private byte[] _buffer;
    
    public SocketReceiveContext(SocketAsyncEventArgs e, byte[] buffer)
    {
        _buffer = buffer;
        SocketEventArgs = e;
        
        OriginOffset = e.Offset;
        SocketEventArgs.Completed += OnReceiveCompleted;
    }

    public SocketAsyncEventArgs SocketEventArgs { get; }
    public int OriginOffset { get; }

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

    public void Dispose()
    {
        if (_disposed) return;

        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
        
        SocketEventArgs.Completed -= OnReceiveCompleted;
        SocketEventArgs.Dispose();
        
        _disposed = true;
    }
}
