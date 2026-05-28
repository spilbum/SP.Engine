using System;
using System.Buffers;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core;
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
    private EngineCore _engine;
    private IPolicySnapshot _policySnapshot;
    private ReadWriteBuffer _readWriteBuffer;
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
    
    public virtual void Initialize(EngineCore engineCore, TcpNetworkSession ns)
    {
        _engine = engineCore;
        _tcpNetworkSession = ns;
        _tcpNetworkSession.Session = this;
        _router.Bind(new ReliableChannel(ns));
        _readWriteBuffer = new ReadWriteBuffer(_engine.Config.Network.ReceiveBufferSize);
        _policySnapshot = _engine.CreatePolicySnapshot(new PolicyGlobals(false, false, 0, 65536));
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

    public bool Send(ChannelKind channel, IMessage message)
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
    
    internal void UpdateUdpNetworkSession(Socket socket, IPEndPoint remoteEndPoint)
    {
        var ns = _udpNetworkSession;
        if (ns == null)
        {
            lock (_udpLock)
            {
                ns ??= new UdpNetworkSession(this, socket, remoteEndPoint);
                
                _udpNetworkSession = ns;
                _router.Bind(new UnreliableChannel(ns));
                
                var assembler = new FragmentAssembler(
                    Config.Network.FragmentAssemblerCleanupPeriodSec,
                    Config.Network.FragmentAssemblerPendingMessageThreshold);
                Interlocked.Exchange(ref _fragmentAssembler, assembler);
            }
        }

        ns.UpdateRemoteEndPoint(remoteEndPoint);
    }

    internal void CloseUdpChannel(CloseReason reason)
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

        OnUdpChannelClosed(reason);
    }

    protected virtual void OnUdpChannelClosed(CloseReason reason)
    {
        
    }

    internal void SetMaxFragmentSize(ushort mtu)
    {
        _udpNetworkSession?.SetMaxFragmentSize(mtu);
    }
    
    internal void HandleUdpMessage(UdpHeader header, IMemoryOwner<byte> bufferOwner)
    {
        try
        {
            if (header.IsFragmented)
            {
                try
                {
                    var assembler = Volatile.Read(ref _fragmentAssembler);
                    if (assembler == null) return;
                    
                    var payloadSpan = bufferOwner.Memory.Span[header.HeaderLength..];
                    if (assembler.TryPush(header, payloadSpan, out var message))
                    {
                        MessageReceived(message);
                    }
                }
                finally
                {
                    bufferOwner.Dispose();
                }
            }
            else
            {
                var message = new UdpMessage(header, bufferOwner);
                MessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }

    internal void ProcessTcpBuffer(byte[] buffer, int offset, int length)
    {
        if (IsClosed || !_readWriteBuffer.TryWrite(buffer.AsSpan(offset, length))) return;

        try
        {
            while (_readWriteBuffer.TryRead(_policySnapshot, out var header, out var bufferOwner))
            {
                var message = new TcpMessage(header, bufferOwner);
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
        
        Logger.Debug("Session {0} closed. reason: {1}", SessionId, reason);
        
        _router.Unbind(ChannelKind.Reliable);
        _router.Unbind(ChannelKind.Unreliable);
        
        var tcp = Interlocked.Exchange(ref _tcpNetworkSession, null);
        tcp?.Close(reason);

        var udp = Interlocked.Exchange(ref _udpNetworkSession, null);
        udp?.Close(reason);

        _readWriteBuffer.Dispose();
        CloseReason = reason;
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
            _fragmentAssembler?.Dispose();
            _readWriteBuffer.Dispose();
        }
    }
}
