using System;
using System.Net;
using System.Net.Sockets;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

internal static class UdpNetworkListenerFactory
{
    public static INetworkListener Create(ListenerInfo info, IEngineConfig config)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return new UnixUdpNetworkListener(info, config);
        return new WindowsUdpNetworkListener(info, config);
    }
}

internal class UdpReceiveEventArgsFactory(int bufferSize) : IPoolObjectFactory<SocketAsyncEventArgs>
{
    public SocketAsyncEventArgs[] Create(int size)
    {
        var globalBuffer = GC.AllocateArray<byte>(bufferSize * size, pinned: true);
        var contexts = new SocketAsyncEventArgs[size];
        
        for (var i = 0; i < size; i++)
        {
            var e = new SocketAsyncEventArgs();
            e.SetBuffer(globalBuffer, i * bufferSize, bufferSize);
            e.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            contexts[i] = e;
        }
        return contexts;
    }
}

internal abstract class UdpNetworkListenerBase(ListenerInfo info, IEngineConfig config)
    : NetworkListenerBase(info)
{
    protected readonly IEngineConfig Config = config;
    protected ExpandablePool<SocketAsyncEventArgs> ReceiveArgsPool;
    protected volatile bool IsStopping;

    protected void InitializePool()
    {
        ReceiveArgsPool = new ExpandablePool<SocketAsyncEventArgs>();
        ReceiveArgsPool.Initialize(
            Config.Session.MaxConnections,
            Config.Session.MaxConnections * 2,
            new UdpReceiveEventArgsFactory(Config.Network.UdpMaxMtu + 1024));
    }

    protected void StartReceive(Socket socket, SocketAsyncEventArgs e)
    {
        try
        {
            if (IsStopping || socket == null) return;
            if (!socket.ReceiveFromAsync(e))
                OnReceiveCompleted(socket, e);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    protected void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        var socket = (Socket)sender;
        var pending = false;
        
        try
        {
            if (IsStopping) return;
            
            do
            {
                if (e.SocketError != SocketError.Success)
                {
                    if (!IsIgnoreSocketError(e.SocketError))
                        OnError(new SocketException((int)e.SocketError));

                    if (e.SocketError == SocketError.OperationAborted) return;
                }
                else if (e.BytesTransferred > 0)
                {
                    try
                    {
                        var buffer = e.Buffer.AsSpan(e.Offset, e.BytesTransferred);
                        var remoteEndPoint = (IPEndPoint)e.RemoteEndPoint;
                        OnDataReceived(socket, buffer, remoteEndPoint);
                    }
                    catch (Exception ex)
                    {
                        OnError(ex);
                    }
                }

                if (IsStopping) return;
                
                pending = socket.ReceiveFromAsync(e);
            } while (!pending);
        }
        catch (Exception ex)
        {
            if (!IsStopping) OnError(ex);
        }
        finally
        {
            if (!pending)
            {
                ReturnToReceiveArgsPool(e);   
            }
        }
    }
    
    private void ReturnToReceiveArgsPool(SocketAsyncEventArgs e)
    {
        e.Completed -= OnReceiveCompleted;
        ReceiveArgsPool.Return(e);
    }

    private static bool IsIgnoreSocketError(SocketError errorCode)
    {
        return errorCode switch
        {
            SocketError.OperationAborted or SocketError.Interrupted or 
            SocketError.NotSocket or SocketError.ConnectionReset => true,
            _ => false
        };
    }
}
