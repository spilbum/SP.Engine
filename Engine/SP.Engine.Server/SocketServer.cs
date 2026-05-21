using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SP.Core;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

internal class ReceiveContextFactory(int bufferSize) : IPoolObjectFactory<SocketReceiveContext>
{
    public SocketReceiveContext[] Create(int size)
    {
        var globalBuffer = GC.AllocateArray<byte>(bufferSize * size);
        
        var contexts = new SocketReceiveContext[size];
        for (var i = 0; i < size; i++)
        {
            var e = new SocketAsyncEventArgs();
            e.SetBuffer(globalBuffer, i * bufferSize, bufferSize);
            contexts[i] = new SocketReceiveContext(e);
        }
        
        return contexts;
    }
}

internal sealed class SocketServer(EngineCore engine, ListenerInfo[] listenerInfos) : IDisposable
{
    private bool _disposed;
    private byte[] _keepAliveOptionValues;
    private readonly object _udpSessionLock = new();
    private ExpandablePool<SocketReceiveContext> _receiveContextPool;
    
    private List<INetworkListener> Listeners { get; } = new(listenerInfos.Length);
    private ListenerInfo[] ListenerInfos { get; } = listenerInfos;
    private bool IsRunning { get; set; }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    public bool Start()
    {
        if (IsRunning) return false;

        var config = engine.Config;
        _receiveContextPool = new ExpandablePool<SocketReceiveContext>();
        _receiveContextPool.Initialize(
            Math.Max(config.Session.MaxConnections, 512),
            Math.Max(config.Session.MaxConnections * 2, 512),
            new ReceiveContextFactory(config.Network.ReceiveBufferSize));
        
        foreach (var info in ListenerInfos)
        {
            var listener = CreateListener(info, config);
            if (listener == null) continue;
            
            listener.Error += OnListenerError;
            listener.Stopped += OnListenerStopped;
            listener.NewClientAccepted += OnNewClientAccepted;
            listener.DataReceived += OnDataReceived;

            if (listener.Start())
            {
                Listeners.Add(listener);
                engine.Logger.Info("Listener ({0}:{1}) was started.", listener.EndPoint, listener.Mode);
            }
            else
            {
                engine.Logger.Error("Listener ({0}:{1}) failed to start.", listener.EndPoint, listener.Mode);

                foreach (var t in Listeners)
                    t.Stop();

                Listeners.Clear();
                return false;
            }
        }

        if (!SetupKeepAlive(config))
            return false;

        IsRunning = true;
        return true;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        foreach (var listener in Listeners)
            listener.Stop();

        Listeners.Clear();
        _receiveContextPool.Dispose();
    }

    private bool SetupKeepAlive(IEngineConfig config)
    {
        try
        {
            var isKeepAlive = config.Network.EnableKeepAlive ? 1 : 0;
            var keepAliveTime = (uint)config.Network.KeepAliveTimeSec * 1000;
            var keepAliveInterval = (uint)config.Network.KeepAliveIntervalSec * 1000;
            const uint dummy = 0;
            _keepAliveOptionValues = new byte[Marshal.SizeOf(dummy) * 3];
            BitConverter.GetBytes((uint)isKeepAlive).CopyTo(_keepAliveOptionValues, 0); // 활성화 (1=true)
            BitConverter.GetBytes(keepAliveTime).CopyTo(_keepAliveOptionValues, Marshal.SizeOf(dummy)); // 유휴 시간 (ms)
            BitConverter.GetBytes(keepAliveInterval)
                .CopyTo(_keepAliveOptionValues, Marshal.SizeOf(dummy) * 2); // Keep-Alive 패킷 간격 (ms)
            return true;
        }
        catch (Exception ex)
        {
            engine.Logger.Fatal("An exception occurred: {0}\r\nstackTrace: {1}", ex.Message, ex.StackTrace);
            return false;
        }
    }

    private static INetworkListener CreateListener(ListenerInfo info, IEngineConfig config)
    {
        return info.Mode switch
        {
            SocketMode.Tcp => new TcpNetworkListener(info),
            SocketMode.Udp => UdpNetworkListenerFactory.Create(info, config),
            _ => throw new ArgumentException($"Invalid socket mode: {info.Mode}")
        };
    }

    private void OnNewClientAccepted(Socket socket)
    {
        if (!IsRunning) return;
        ProcessTcpClient(socket);
    }
    
    private void OnDataReceived(Socket socket, ReadOnlySpan<byte> buffer, IPEndPoint remoteEndPoint)
    {
        if (!UdpHeader.TryRead(buffer, out var header, out var headerConsumed)) return;
            
        var session = engine.GetSession(header.SessionId);
        if (session == null || session.IsClosed) return;

        if (session.UdpNetworkSession == null)
        {
            lock (_udpSessionLock)
            {
                if (session.UdpNetworkSession == null)
                {
                    var ns = new UdpNetworkSession(session, socket, remoteEndPoint);
                    session.UdpNetworkSession = ns;
                }
            }
        }

        if (session.UdpNetworkSession == null) return;
        session.UdpNetworkSession.UpdateRemoteEndPoint(remoteEndPoint);
        
        var bodyBuffer = new PooledBuffer(header.BodyLength);
        buffer.Slice(headerConsumed, header.BodyLength).CopyTo(bodyBuffer.Memory.Span);

        var state = (session, header, bodyBuffer);
        
        session.AsyncRun(state, static s =>
        {
            try
            {
                s.session.HandleUdpMessage(s.header, s.bodyBuffer.Memory.Span);
            }
            finally
            {
                s.bodyBuffer.Dispose();
            }
        });
    }

    private void ProcessTcpClient(Socket client)
    {
        if (!_receiveContextPool.TryRent(out var context))
        {
            engine.Logger.Warn("Failed to rent receive context. (Count: {0})", _receiveContextPool.ItemCount);
            engine.AsyncRun(client.SafeClose);
            return;
        }

        var ns = new TcpNetworkSession(client, context);
        ns.Closed += OnTcpSessionClosed;

        var session = CreateSession(client, ns);
        if (session != null)
        {
            engine.AsyncRun(ns.Start);
        }
        else
        {
            ns.Close(CloseReason.ApplicationError);
        }
    }

    private void OnTcpSessionClosed(INetworkSession session, CloseReason reason)
    {
        if (session is not TcpNetworkSession s) return;
        
        var context = s.ReceiveContext;
        if (context != null)
        {
            context.Reset();
            _receiveContextPool.Return(context);
        }
    }

    private void OnListenerStopped(object sender, EventArgs e)
    {
        if (sender is INetworkListener listener)
            engine.Logger.Info("Listener ({0}) was stopped.", listener.EndPoint);
    }

    private void OnListenerError(INetworkListener listener, Exception e)
    {
        engine.Logger.Error($"Listener ({listener.EndPoint}) error: {e.Message}\r\nstackTrace={e.StackTrace}");
    }

    private Session CreateSession(Socket client, TcpNetworkSession networkSession)
    {
        var config = engine.Config;
        if (0 < config.Network.SendTimeoutMs)
            client.SendTimeout = config.Network.SendTimeoutMs;

        if (0 < config.Network.ReceiveBufferSize)
            client.ReceiveBufferSize = config.Network.ReceiveBufferSize;

        if (0 < config.Network.SendBufferSize)
            client.SendBufferSize = config.Network.SendBufferSize;

        client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _keepAliveOptionValues);

        var lingerOption = new LingerOption(true, 0); // 즉시 닫기
        client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, lingerOption);
        client.NoDelay = true;
        return engine.CreateSession(networkSession);
    }
}
