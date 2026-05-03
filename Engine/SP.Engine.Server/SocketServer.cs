using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public class ReceiveContextFactory : IPoolObjectFactory<SocketReceiveContext>
{
    private readonly int _bufferSize;

    public ReceiveContextFactory(int bufferSize)
    {
        if (bufferSize <= 0)
            throw new ArgumentException("Buffer size must be greater than zero.");
        _bufferSize = bufferSize;
    }

    public SocketReceiveContext[] Create(int size)
    {
        var contexts = new SocketReceiveContext[size];

        for (var i = 0; i < size; i++)
        {
            var buffer = new byte[_bufferSize];

            var e = new SocketAsyncEventArgs();
            e.SetBuffer(buffer, 0, _bufferSize);
            
            contexts[i] = new SocketReceiveContext(e);
        }
        
        return contexts;
    }
}

public class TcpSendingQueueFactory(int queueCapacity) : IPoolObjectFactory<SegmentQueue>
{
    public SegmentQueue[] Create(int size)
    {
        var source = new ArraySegment<byte>[size * queueCapacity];
        var items = new SegmentQueue[size];

        for (var i = 0; i < size; i++)
            items[i] = new SegmentQueue(source, i * queueCapacity, queueCapacity);

        return items;
    }
}

public class UdpSocketEventArgsCreator : IPoolObjectFactory<SocketAsyncEventArgs>
{
    public SocketAsyncEventArgs[] Create(int size)
    {
        var items = new SocketAsyncEventArgs[size];
        for (var i = 0; i < size; i++)
            items[i] = new SocketAsyncEventArgs();
        return items;
    }
}

internal sealed class SocketServer(IBaseEngine engine, ListenerInfo[] listenerInfos) : IDisposable
{
    private bool _disposed;
    private byte[] _keepAliveOptionValues;
    private ExpandablePool<SocketReceiveContext> _tcpReceiveContextPool;
    private ExpandablePool<SegmentQueue> _tcpSendingQueuePool;
    private ExpandablePool<SocketAsyncEventArgs> _udpEventArgsPool;
    
    private List<ISocketListener> Listeners { get; } = new(listenerInfos.Length);
    private ListenerInfo[] ListenerInfos { get; } = listenerInfos;
    private bool IsStopped { get; set; }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    public bool Start()
    {
        IsStopped = false;

        var config = engine.Config;

        _tcpReceiveContextPool = new ExpandablePool<SocketReceiveContext>();
        _tcpReceiveContextPool.Initialize(
            config.Session.MaxConnections,
            config.Session.MaxConnections,
            new ReceiveContextFactory(config.Network.ReceiveBufferSize));

        _tcpSendingQueuePool = new ExpandablePool<SegmentQueue>();
        _tcpSendingQueuePool.Initialize(Math.Max(config.Session.MaxConnections / 2, 512)
            , Math.Max(config.Session.MaxConnections * 3, 512)
            , new TcpSendingQueueFactory(config.Network.SendingQueueSize));
        
        _udpEventArgsPool = new ExpandablePool<SocketAsyncEventArgs>();
        _udpEventArgsPool.Initialize(
            Math.Max(config.Session.MaxConnections / 4, 256),
            config.Session.MaxConnections,
            new UdpSocketEventArgsCreator());

        foreach (var info in ListenerInfos)
        {
            var listener = CreateListener(info);
            if (listener == null) continue;
            
            listener.Error += OnListenerError;
            listener.Stopped += OnListenerStopped;
            listener.NewClientAccepted += OnNewClientAccepted;

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

        return true;
    }

    public void Stop()
    {
        IsStopped = true;

        foreach (var listener in Listeners)
            listener.Stop();

        Listeners.Clear();
        _tcpReceiveContextPool.Dispose();
        _tcpSendingQueuePool.Dispose();
        _udpEventArgsPool.Dispose();
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

    private static ISocketListener CreateListener(ListenerInfo listenerInfo)
    {
        return listenerInfo.Mode switch
        {
            SocketMode.Tcp => new TcpNetworkListener(listenerInfo),
            SocketMode.Udp => new UdpNetworkListener(listenerInfo),
            _ => throw new ArgumentException($"Invalid socket mode: {listenerInfo.Mode}")
        };
    }

    private void OnNewClientAccepted(ISocketListener listener, Socket socket, object state)
    {
        if (IsStopped)
            return;

        switch (listener.Mode)
        {
            case SocketMode.Tcp:
                ProcessTcpClient(socket);
                break;
            case SocketMode.Udp:
                ProcessUdpClient(socket, state);
                break;
            default:
                throw new NotSupportedException($"{listener.Mode}");
        }
    }

    private void ProcessUdpClient(Socket socket, object state)
    {
        if (state is not (ArraySegment<byte> segment, IPEndPoint remoteEndPoint)) return;

        var header = UdpHeader.Read(segment);
        var session = ((BaseEngine)engine).GetSession(header.SessionId);
        if (session == null || session.IsClosed) return;

        if (session.UdpSession == null)
        {
            lock (session)
            {
                if (session.UdpSession == null)
                {
                    var ns = new UdpNetworkSession(socket, remoteEndPoint, _udpEventArgsPool);
                    ns.SetupAssembler(engine.Config.Network.UdpAssemblyTimeoutSec, engine.Config.Network.UdpMaxPendingMessageCount);
                    ns.Session = session;
                    session.BindUdpSession(ns);
                    engine.Logger.Debug("UDP Session '{0}' bound.", session.SessionId);
                }
            }
        }

        if (session.UdpSession == null) return;
        
        // 이동통신망(LTE/5G <-> Wi-Fi) 전환 등으로 인한 IP 변경 대응
        session.UdpSession.UpdateRemoteEndPoint(remoteEndPoint);
        session.ProcessUdpBuffer(segment.Array, segment.Offset, segment.Count);
    }

    private void ProcessTcpClient(Socket client)
    {
        if (!_tcpReceiveContextPool.TryRent(out var context))
        {
            engine.AsyncRun(client.SafeClose);
            engine.Logger.Error("Limit connection count {0} was reached.", engine.Config.Session.MaxConnections);
            return;
        }

        var ns = new TcpNetworkSession(client, context, _tcpSendingQueuePool);
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
        if (session is not ITcpNetworkSession s) return;
        var context = s.ReceiveContext;
        if (context == null) return;
        context.Reset();
        _tcpReceiveContextPool?.Return(context);
    }

    private void OnListenerStopped(object sender, EventArgs e)
    {
        if (sender is ISocketListener listener)
            engine.Logger.Info("Listener ({0}) was stopped.", listener.EndPoint);
    }

    private void OnListenerError(ISocketListener listener, Exception e)
    {
        engine.Logger.Error($"Listener ({listener.EndPoint}) error: {e.Message}\r\nstackTrace={e.StackTrace}");
    }

    private IBaseSession CreateSession(Socket client, TcpNetworkSession networkSession)
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
