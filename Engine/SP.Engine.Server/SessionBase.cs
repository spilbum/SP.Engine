using System;
using System.Buffers;
using System.Net;
using System.Threading;
using SP.Core;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
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
    private SessionReceiveBuffer _receiveBuffer;
    private long _sessionId;
    private volatile TcpNetworkSession _tcpSession;
    private long _lastActiveTimeTicks;
    private UdpFragmentAssembler _udpFragmentAssembler;
    private volatile UdpNetworkSession _udpSession;
    protected volatile int _state = (int)SessionState.None;
    
    public bool IsPaused => _tcpSession?.IsPaused ?? true;
    public void PauseReceive() => _tcpSession?.PauseReceive();
    public void ResumeReceive() => _tcpSession?.ResumeReceive();

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

    public IPEndPoint LocalEndPoint => _tcpSession?.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _tcpSession?.RemoteEndPoint;
    
    public ILogger Logger => _engine?.Logger;
    public IEngineConfig Config => _engine?.Config;

    public UdpNetworkSession UdpSession
    {
        get => _udpSession;
        internal set
        {
            _udpSession = value;
            _udpFragmentAssembler = new UdpFragmentAssembler(
                Config.Network.UdpCleanupPeriodSec,
                Config.Network.UdpMaxPendingMessageCount);
            _router.Bind(new UnreliableChannel(value));
        }
    }
    
    public DateTime StartTime { get; }
    public CloseReason CloseReason { get; private set; }
    public bool IsAuthenticated { get; protected set; }
    public bool IsClosing => (SessionState)_state == SessionState.Closing;
    public bool IsClosed => (SessionState)_state == SessionState.Closed;
    
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
        _tcpSession = ns;
        _tcpSession.Session = this;
        _router.Bind(new ReliableChannel(ns));
        _receiveBuffer = new SessionReceiveBuffer(engineCore.Config.Network.ReceiveBufferSize);
    }

    TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
        => message.Deserialize<TProtocol>(null, null);

    public void CleanupFragmentAssembler()
    {
        if (_udpSession == null) return;
        
        var now = DateTime.UtcNow;
        _udpFragmentAssembler.Cleanup(now);
    }

    protected void EnableUdp()
        => _router.SetUdpAvailable(true);
    
    protected void DisableUdp()
        => _router.SetUdpAvailable(false);
    
    public bool IsUdpAvailable => _router.IsUdpAvailable;

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

    public void ProcessTcpBuffer(byte[] buffer, int offset, int length)
    {
        if (IsClosed || !_receiveBuffer.Write(buffer.AsSpan(offset, length))) return;

        try
        {
            while (_receiveBuffer.TryExtract(Config.Network.MaxFrameBytes, out var header, out var bodyOwner, out var bodyLength))
            {
                var message = new TcpMessage(header, bodyOwner, bodyLength);
                MessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Close(CloseReason.ProtocolError);
        }
    }
    
    public void HandleUdpMessage(UdpHeader header, ReadOnlySpan<byte> bodyData)
    {
        try
        {
            if (header.IsFragmented)
            {
                if (_udpFragmentAssembler.TryPush(header, bodyData, out var message))
                {
                    MessageReceived(message);
                }
            }
            else
            {
                IMemoryOwner<byte> owner = null;
                if (header.BodyLength > 0)
                {
                    var pooled = new PooledBuffer(header.BodyLength);
                    bodyData.CopyTo(pooled.Memory.Span);
                    owner = pooled;
                }
            
                var message = new UdpMessage(header, owner, header.BodyLength);
                MessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
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
        
        var tcp = Interlocked.Exchange(ref _tcpSession, null);
        if (tcp != null)
        {
            tcp.Close(reason);
            _router.Unbind(ChannelKind.Reliable);
        }
        
        var udp = Interlocked.Exchange(ref _udpSession, null);
        if (udp != null)
        {
            udp.Close(reason);
            _router.Unbind(ChannelKind.Unreliable);
        }

        if (_receiveBuffer != null)
        {
            _receiveBuffer.Dispose();
            _receiveBuffer = null;   
        }
        
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
            _tcpSession?.Close(CloseReason.ServerClosing);
            _udpSession?.Close(CloseReason.ServerClosing);
            _udpFragmentAssembler?.Dispose();
            _receiveBuffer?.Dispose();
        }
    }
}
