using System;
using System.Net;
using System.Threading;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public interface IBaseSession : ICommandContext
{
    public long SessionId { get; }
}

public abstract class BaseSession : IBaseSession, IDisposable
{
    private readonly MessageChannelRouter _router = new();
    private BaseEngine _engine;
    private SessionReceiveBuffer _receiveBuffer;
    private long _sessionId;
    private TcpNetworkSession _tcpSession;

    public UdpNetworkSession UdpSession { get; private set; }
    
    public bool IsPaused => _tcpSession?.IsPaused ?? false;
    public void PauseReceive() => _tcpSession?.PauseReceive();
    public void ResumeReceive() => _tcpSession?.ResumeReceive();

    public long SessionId
    {
        get => Interlocked.Read(ref _sessionId);
        private set => Interlocked.Exchange(ref _sessionId, value);
    }

    public IPEndPoint LocalEndPoint => _tcpSession?.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _tcpSession?.RemoteEndPoint;
    public bool IsClosed => _tcpSession?.IsClosed ?? false;
    public ILogger Logger => _engine?.Logger;
    public IEngineConfig Config => _engine?.Config;
    public DateTime LastActiveTime { get; private set; }
    public DateTime StartTime { get; }
    public CloseReason CloseReason { get; private set; }
    public bool IsAuthenticated { get; protected set; }
    public bool IsClosing { get; protected set; }
    public DateTime StartClosingTime { get; protected set; }
    
    protected BaseSession()
    {
        StartTime = DateTime.UtcNow;
        LastActiveTime = StartTime;
    }
    
    public virtual void Initialize(long sessionId, IBaseEngine baseEngine, TcpNetworkSession tcpSession)
    {
        SessionId = sessionId;
        _engine = (BaseEngine)baseEngine;
        _tcpSession = tcpSession;
        _router.Bind(new ReliableChannel(tcpSession));
        _receiveBuffer = new SessionReceiveBuffer(Config.Network.ReceiveBufferSize);
    }
    
    TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
        => message.Deserialize<TProtocol>(null, null);

    public void BindUdpSession(UdpNetworkSession udpSession)
    {
        UdpSession = udpSession;
    }

    protected void EnableUdp()
        => _router.SetUdpAvailable(true);
    
    protected void DisableUdp()
        => _router.SetUdpAvailable(false);
    
    public bool IsUdpAvailable => _router.IsUdpAvailable;

    public bool TrySend(ChannelKind channel, IMessage message)
    {
        switch (channel)
        {
            case ChannelKind.Reliable when message is not TcpMessage:
            case ChannelKind.Unreliable when message is not UdpMessage:
                return false;
            default:
                if (!_router.TrySend(channel, message)) return false;
                LastActiveTime = DateTime.UtcNow;
                return true;
        }
    }

    public void ProcessTcpBuffer(byte[] data, int offset, int length)
    {
        if (!_receiveBuffer.Write(data.AsSpan(offset, length))) return;

        try
        {
            while (_receiveBuffer.TryExtract(Config.Network.MaxFrameBytes, out var header, out var bodyOwner))
            {
                var message = new TcpMessage(header, bodyOwner);
                MessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Close(CloseReason.ProtocolError);
        }
    }

    public void ProcessUdpBuffer(byte[] data, int offset, int length)
    {
        if (IsClosed || UdpSession == null) return;

        try
        {
            if (UdpSession.Assembler.TryPush(data.AsSpan(offset, length), out var message))
            {
                MessageReceived(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error processing UDP buffer. sessionId={0}", SessionId);
        }
    }

    protected virtual void MessageReceived(IMessage message)
    {
        LastActiveTime = DateTime.UtcNow;
    }

    protected void SetMtu(ushort mtu)
    {
        UdpSession?.SetMtu(mtu);
    }

    public virtual void Close(CloseReason reason)
    {
        if (_tcpSession != null)
        {
            _router.Unbind(ChannelKind.Reliable);
            _tcpSession.Close(reason);
            _tcpSession = null;
        }

        if (UdpSession != null)
        {
            _router.Unbind(ChannelKind.Unreliable);
            UdpSession.Close(reason);
            UdpSession = null;
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
            _receiveBuffer?.Dispose();
        }
    }
}
