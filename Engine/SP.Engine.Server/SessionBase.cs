using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core.Buffers;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public enum SessionState
{
    None = 0,
    Connected,
    Closing,
    Closed
}

public abstract class SessionBase : ICommandContext, IDisposable
{
    private readonly MessageChannelRouter _router = new();
    private ReadWriteBuffer _readWriteBuffer;
    private EngineCore _engine;
    private IPolicySnapshot _policySnapshot;
    private long _sessionId;
    private volatile TcpNetworkSession _tcpNetworkSession;
    private long _lastActiveTimeTicks;
    private FragmentAssembler _fragmentAssembler;
    private volatile UdpNetworkSession _udpNetworkSession;
    private readonly object _udpLock = new();
    
    protected volatile int _state = (int)SessionState.None;

    public long SessionId
    {
        get => Interlocked.Read(ref _sessionId);
        private init => Interlocked.Exchange(ref _sessionId, value);
    }
    
    public long LastActiveTimeTicks
    {
        get => Interlocked.Read(ref _lastActiveTimeTicks);
        private set => _lastActiveTimeTicks = value;
    }

    public IPEndPoint LocalEndPoint => _tcpNetworkSession?.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _tcpNetworkSession?.RemoteEndPoint;
    
    public ILogger Logger => _engine?.Logger;
    public IEngineConfig Config => _engine?.Config;
    public IPolicySnapshot PolicySnapshot => _policySnapshot;
    
    public DateTime StartTime { get; }
    public CloseReason CloseReason { get; private set; }
    public bool IsAuthenticated { get; protected set; }
    public bool IsClosing => (SessionState)_state == SessionState.Closing;
    public bool IsClosed => (SessionState)_state == SessionState.Closed;

    IEncryptor ICommandContext.Encryptor => null;
    ICompressor ICommandContext.Compressor => null;
    
    public DateTime StartClosingTime { get; protected set; }
    
    protected SessionBase(long sessionId)
    {
        SessionId = sessionId;
        StartTime = DateTime.UtcNow;
        LastActiveTimeTicks = DateTime.UtcNow.Ticks;
    }
    
    public virtual void Initialize(EngineCore engine, TcpNetworkSession ns, ReadWriteBuffer readWriteBuffer)
    {
        _engine = engine;
        _tcpNetworkSession = ns;
        _tcpNetworkSession.Session = this;
        _router.Bind(new ReliableChannel(ns));
        _readWriteBuffer = readWriteBuffer;
        _policySnapshot = _engine.CreatePolicySnapshot(new PolicyGlobals(false, false, 0, 65536));
    }

    public ReadWriteBuffer ReleaseReadWriteBuffer()
    {
        var buffer = Interlocked.Exchange(ref _readWriteBuffer, null);
        buffer?.Clear();
        return buffer;
    }

    public void CleanupFragmentAssembler()
    {
        var assembler = Volatile.Read(ref _fragmentAssembler);
        if (assembler == null) return;

        var now = DateTime.UtcNow;
        assembler.Cleanup(now);
    }

    protected void EnableUdp()
        => _router.SetUdpAvailable(true);
    
    protected void DisableUdp()
        => _router.SetUdpAvailable(false);
    
    public bool IsUdpAvailable => _router.IsUdpAvailable;
    
    protected void SetupProtocolPolicy()
    {
        var n = Config.Network;
        var g = new PolicyGlobals(n.UseEncrypt, n.UseCompress, n.CompressionThreshold, n.MaxPayloadLength);
        var snapshot = _engine.CreatePolicySnapshot(g);
        Interlocked.Exchange(ref _policySnapshot, snapshot);
    }

    public bool TrySend(ChannelKind channel, IMessage message)
    {
        switch (channel)
        {
            case ChannelKind.Reliable when message is not TcpMessage:
            case ChannelKind.Unreliable when message is not UdpMessage:
                return false;
            default:
                if (!_router.TrySend(channel, message)) return false;
                LastActiveTimeTicks = DateTime.UtcNow.Ticks;
                return true;
        }
    }

    internal void SetMaxFragmentSize(ushort mtu)
    {
        _udpNetworkSession?.SetMaxFragmentSize(mtu);
    }
    
    internal void ProcessUdpClient(
        Socket socket,
        IPEndPoint remoteEndPoint,
        UdpHeader header,
        BufferOwner buffer)
    {
        try
        {
            var ns = _udpNetworkSession;
            if (ns == null)
            {
                lock (_udpLock)
                {
                    ns = _udpNetworkSession;
                    if (ns == null)
                    {
                        ns = new UdpNetworkSession(this, socket, remoteEndPoint);
                        _udpNetworkSession = ns;
                        _router.Bind(new UnreliableChannel(ns));
                        
                        var assembler = new FragmentAssembler(
                            Config.Network.FragmentAssemblerCleanupPeriodSec,
                            Config.Network.FragmentAssemblerPendingMessageThreshold);
                        Interlocked.Exchange(ref _fragmentAssembler, assembler);
                    
                        Logger.Debug("Successfully initialized UDP Network Session for SessionId={0}", SessionId);
                    }
                }
            }
            
            ns.UpdateContext(socket, remoteEndPoint);
            
            if (header.IsFragmented)
            {
                var assembler = Volatile.Read(ref _fragmentAssembler);
                if (assembler == null)
                {
                    buffer.Dispose();
                    return;
                }
 
                if (assembler.TryProcessFragment(header, buffer, out var message))
                {
                    MessageReceived(message);
                }
            }
            else
            {
                var message = MessagePool<UdpMessage>.Rent();
                message.Initialize(header, buffer);
                MessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            buffer.Dispose();
        }
    }
    
    private void CloseUdpNetworkSession(CloseReason reason)
    {
        _router.Unbind(ChannelKind.Unreliable);

        lock (_udpLock)
        {
            var udp = Interlocked.Exchange(ref _udpNetworkSession, null);
            if (udp == null) return;
            udp.Close(reason);
            
            var assembler = Interlocked.Exchange(ref _fragmentAssembler, null);
            assembler?.Dispose();
        }

        OnUdpClosed(reason);
    }

    protected virtual void OnUdpClosed(CloseReason reason)
    {
        
    }

    internal void ProcessTcpBuffer(byte[] buffer, int offset, int length)
    {
        if (IsClosed) return;

        var rwBuffer = Volatile.Read(ref _readWriteBuffer);
        if (rwBuffer == null) return;

        var data = buffer.AsSpan(offset, length);
        if (!rwBuffer.TryWrite(data))
        {
            Close(CloseReason.InternalError);
            return;
        }

        try
        {
            while (Volatile.Read(ref _readWriteBuffer) != null &&
                   rwBuffer.TryRead(_policySnapshot, out var header, out var bufferOwner))
            {
                var message = MessagePool<TcpMessage>.Rent();
                message.Initialize(header, bufferOwner);
                MessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Close(CloseReason.ProtocolError);
        }
    }
    
    protected virtual void MessageReceived(IMessage message)
    {
        LastActiveTimeTicks = DateTime.UtcNow.Ticks;
    }

    public virtual void Close(CloseReason reason)
    {
        if (Interlocked.Exchange(ref _state, (int)SessionState.Closed) == (int)SessionState.Closed) return;
        CloseReason = reason;
        
        _router.Unbind(ChannelKind.Reliable);
        _router.Unbind(ChannelKind.Unreliable);
        
        var tcp = Interlocked.Exchange(ref _tcpNetworkSession, null);
        if (tcp is { IsClosed: false })
        {
            tcp.Close(reason);
        }
        
        CloseUdpNetworkSession(reason);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!IsClosed)
            {
                Close(CloseReason.ServerClosing);
            }
            
            _fragmentAssembler?.Dispose();
        }
    }
}
