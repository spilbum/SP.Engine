using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core;
using SP.Core.Logging;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public interface IBaseSession : ILogContext
{
    IBaseEngine Engine { get; }
    IEngineConfig Config { get; }
    void ProcessTcpBuffer(byte[] buffer, int offset, int length);
}

public abstract class BaseSession : IBaseSession
{
    private readonly MessageChannelRouter _channelRouter = new();
    private IDisposable _assemblerCleanupTimer;
    private BaseEngine _engine;
    private SessionReceiveBuffer _receiveBuffer;
    private long _sessionId;
    
    public long SessionId
    {
        get => Interlocked.Read(ref _sessionId);
        internal set => Interlocked.Exchange(ref _sessionId, value);
    }

    protected BaseSession()
    {
        StartTime = DateTime.UtcNow;
        LastActiveTime = StartTime;
    }

    public IPEndPoint LocalEndPoint => NetworkSession.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => NetworkSession.RemoteEndPoint;
    public DateTime StartTime { get; }
    public DateTime StartClosingTime { get; protected set; }
    public bool IsAuthHandshake { get; protected set; }
    public UdpSocket UdpSocket { get; private set; }
    public CloseReason CloseReason { get; private set; }
    public bool IsConnected { get; internal set; }
    public bool IsClosed { get; internal set; }
    public int Index { get; internal set; }
    IBaseEngine IBaseSession.Engine => _engine;
    public IEngineConfig Config => _engine.Config;
    public ILogger Logger => _engine.Logger;
    public DateTime LastActiveTime { get; private set; }
    public INetworkSession NetworkSession { get; private set; }

    protected void EnableUdp()
        => _channelRouter.SetUdpAvailable(true);
    
    protected void DisableUdp()
        => _channelRouter.SetUdpAvailable(false);
    
    public bool IsUdpAvailable => _channelRouter.IsUdpAvailable;

    public virtual void Close(CloseReason reason)
    {
        _channelRouter.Unbind(ChannelKind.Reliable);
        _channelRouter.Unbind(ChannelKind.Unreliable);
        _receiveBuffer?.Dispose();
        NetworkSession.Close(reason);
        UdpSocket?.Close(reason);
        StopFragmentAssemblerCleanupScheduler();
        CloseReason = reason;
    }

    public virtual void Initialize(long sessionId, int sessionIndex, IBaseEngine engine,
        TcpNetworkSession networkSession)
    {
        SessionId = sessionId;
        Index = sessionIndex;
        NetworkSession = networkSession;
        _engine = (BaseEngine)engine;
        _receiveBuffer = new SessionReceiveBuffer(engine.Config.Network.ReceiveBufferSize);
        networkSession.Attach(this);
        _channelRouter.Bind(new ReliableChannel(networkSession));
        IsConnected = true;
    }

    public bool TrySend(ChannelKind channel, IMessage message)
    {
        switch (channel)
        {
            case ChannelKind.Reliable when message is not TcpMessage:
            case ChannelKind.Unreliable when message is not UdpMessage:
                return false;
            default:
                if (!_channelRouter.TrySend(channel, message)) return false;
                LastActiveTime = DateTime.UtcNow;
                return true;
        }
    }

    private void OnReceivedMessage(IMessage message)
    {
        try
        {
            ExecuteMessage(message);
        }
        finally
        {
            LastActiveTime = DateTime.UtcNow;
        }
    }

    private void StartFragmentAssemblerCleanupScheduler()
    {
        var sec = Config.Session.FragmentAssemblerCleanupTimeoutSec;
        var timeout = TimeSpan.FromSeconds(sec);
        var period = TimeSpan.FromSeconds(sec / 2.0);
        _assemblerCleanupTimer = _engine.Scheduler.Schedule(_engine.Fiber,
            UdpSocket.Assembler.Cleanup,
            timeout,
            period,
            period);
    }

    private void StopFragmentAssemblerCleanupScheduler()
    {
        _assemblerCleanupTimer?.Dispose();
    }

    private void EnsureUdpSocket(Socket socket, IPEndPoint remoteEndPoint)
    {
        if (UdpSocket == null)
        {
            lock (this)
            {
                if (UdpSocket == null)
                {
                    UdpSocket = new UdpSocket(socket, remoteEndPoint);
                    UdpSocket.Attach(this);
                    _channelRouter.Bind(new UnreliableChannel(UdpSocket));
                    StartFragmentAssemblerCleanupScheduler();
                    Logger.Debug("Session {0} UDP Socket Created: {1}", SessionId, remoteEndPoint);
                    return;
                }
            }
        }

        if (UdpSocket.RemoteEndPoint.Equals(remoteEndPoint))
            return;

        lock (this)
        {
            if (UdpSocket.RemoteEndPoint.Equals(remoteEndPoint))
                return;

            Logger.Debug("Session {0} UDP EndPoint Changed: {1} -> {2}",
                SessionId, UdpSocket.RemoteEndPoint, remoteEndPoint);
            UdpSocket.UpdateRemoteEndPoint(remoteEndPoint);
        }
    }

    public void ProcessUdpBuffer(UdpHeader header, ArraySegment<byte> segment, Socket socket, IPEndPoint remoteEndPoint)
    {
        if (header.Id is C2SEngineProtocolId.UdpHelloReq 
            or C2SEngineProtocolId.UdpHealthCheckReq 
            or C2SEngineProtocolId.UdpHealthCheckConfirm)
        {
            EnsureUdpSocket(socket, remoteEndPoint);
        }
        else
        {
            if (UdpSocket != null && !UdpSocket.RemoteEndPoint.Equals(remoteEndPoint))
            {
                // 일반 데이터 패킷인데 주소가 다르다면 무시.
                return;
            }
        }

        if (UdpSocket == null) return;

        if (header.Fragmented == 0x01)
        {
            var bodySpan = segment.AsSpan(0, header.BodyLength);
            if (!FragmentHeader.TryParse(bodySpan, out var fHeader, out var consumed)) return;

            var fSegment = new ArraySegment<byte>(segment.Array!, consumed, fHeader.FragLength);
            
            if (!UdpSocket.Assembler.TryAssemble(fHeader, fSegment, out var body)) return;

            var normalizedHeader = new UdpHeaderBuilder()
                .From(header)
                .WithBodyLength(body.Length)
                .Build();

            var message = new UdpMessage(normalizedHeader, body);
            OnReceivedMessage(message);
        }
        else
        {
            if (header.BodyLength > 0)
            {
                var body = new RentedBuffer(header.BodyLength);
                segment.AsSpan(0, header.BodyLength).CopyTo(body.Span);
                var message = new UdpMessage(header, body);
                OnReceivedMessage(message);
            }
            else
            {
                var message = new UdpMessage(header, RentedBuffer.Empty);
                OnReceivedMessage(message);
            }
        }
    }
    
    void IBaseSession.ProcessTcpBuffer(byte[] buffer, int offset, int length)
    {
        if (!_receiveBuffer.TryWrite(new ReadOnlySpan<byte>(buffer, offset, length))) return;

        try
        {
            const int headerSize = TcpHeader.ByteSize;
            const int maxProcessPerTick = 150;
            var processedCount = 0;
            
            Span<byte> headerSpan = stackalloc byte[headerSize];

            while (_receiveBuffer.Available >= headerSize || ++processedCount <= maxProcessPerTick)
            {
                if (!_receiveBuffer.TryPeek(headerSpan, headerSize) ||
                    !TcpHeader.TryRead(headerSpan, out var header, out var consumed))
                {
                    break;
                }

                // 페이로드 검증
                var bodyLength = header.BodyLength;
                if (bodyLength < 0 || bodyLength > Config.Network.MaxFrameBytes)
                {
                    Logger.Warn("Invalid payload length. id={0}, max={1}, len={2}",
                        header.Id, Config.Network.MaxFrameBytes, bodyLength);
                    Close(CloseReason.ProtocolError);
                    break;
                }

                var totalLen = consumed + bodyLength;
                if (_receiveBuffer.Available < totalLen) break;

                // 헤더 소비
                _receiveBuffer.Consume(consumed);

                if (bodyLength > 0)
                {
                    var body = new RentedBuffer(bodyLength);
                    _receiveBuffer.TryPeek(body.Span, bodyLength);
                    _receiveBuffer.Consume(bodyLength);
                    OnReceivedMessage(new TcpMessage(header, body));
                }
                else
                {
                    OnReceivedMessage(new TcpMessage(header, RentedBuffer.Empty));
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
            Close(CloseReason.ProtocolError);
        }
    }

    protected abstract void ExecuteMessage(IMessage message);

    public virtual void Close()
    {
        Close(CloseReason.ServerClosing);
    }
}
