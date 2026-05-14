using System;
using System.Net;
using System.Net.Sockets;

namespace SP.Engine.Server;

internal delegate void ErrorHandler(INetworkListener listener, Exception e);

internal delegate void NewClientAcceptHandler(Socket client);

internal delegate void DataReceivedHandler(Socket listenSocket, ReadOnlySpan<byte> buffer, IPEndPoint remoteEndPoint);

public class ListenerInfo
{
    public IPEndPoint EndPoint { get; init; }
    public int BackLog { get; init; }
    public SocketMode Mode { get; init; }
}

internal interface INetworkListener
{
    SocketMode Mode { get; }
    IPEndPoint EndPoint { get; }
    int BackLog { get; }
    event EventHandler Stopped;
    event ErrorHandler Error;
    event NewClientAcceptHandler NewClientAccepted;
    event DataReceivedHandler DataReceived;

    bool Start();
    void Stop();
}

internal abstract class NetworkListenerBase(ListenerInfo info) : INetworkListener, IDisposable
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public SocketMode Mode { get; set; } = info.Mode;
    public IPEndPoint EndPoint { get; } = info.EndPoint;
    public int BackLog { get; } = info.BackLog;

    public event EventHandler Stopped;
    public event ErrorHandler Error;
    public event NewClientAcceptHandler NewClientAccepted;
    public event DataReceivedHandler DataReceived;

    public abstract bool Start();
    public abstract void Stop();

    protected void OnStopped()
    {
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    protected void OnError(Exception e)
    {
        Error?.Invoke(this, e);
    }

    protected void OnNewClientAccepted(Socket client)
    {
        var handler = NewClientAccepted;
        handler?.Invoke(client);
    }

    protected void OnDataReceived(Socket listenSocket, ReadOnlySpan<byte> buffer, IPEndPoint remoteEndPoint)
    {
        var handler = DataReceived;
        handler?.Invoke(listenSocket, buffer, remoteEndPoint);
    }

    protected virtual void Dispose(bool disposing)
    {
        OnStopped();
    }
}
