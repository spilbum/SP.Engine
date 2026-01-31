using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using SP.Core;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public interface IBaseSession : ILogContext
{
    string Id { get; }
    IBaseEngine Engine { get; }
    INetworkSession NetworkSession { get; }
    IEngineConfig Config { get; }
    DateTime LastActiveTime { get; }
    void ProcessBuffer(byte[] buffer, int offset, int length);
    void ProcessBuffer(PooledBuffer buffer, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint);
    void Close(CloseReason reason);
}

public abstract class BaseSession : IBaseSession
{
    private readonly MessageChannelRouter _channelRouter = new();
    private IDisposable _assemblerCleanupScheduler;
    private BaseEngine _engine;
    private PooledReceiveBuffer _receiveBuffer;
    private IFiberScheduler _scheduler;

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

    IBaseEngine IBaseSession.Engine => _engine;
    public IEngineConfig Config => _engine.Config;
    public ILogger Logger => _engine.Logger;
    public DateTime LastActiveTime { get; private set; }
    public string Id { get; private set; }
    public INetworkSession NetworkSession { get; private set; }

    void IBaseSession.ProcessBuffer(PooledBuffer buffer, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint)
    {
        EnsureUdpSocket(socket, remoteEndPoint);

        var bodyOffset = header.Size;
        
        if (header.Fragmented == 0x01)
        {
            var bodySpan = buffer.Span.Slice(bodyOffset, header.PayloadLength);
            if (!FragmentHeader.TryParse(bodySpan, out var fragHeader, out var consumed))
                return;

            var fragBuffer = new PooledBuffer(fragHeader.FragLength);
            buffer.Span.Slice(bodyOffset + consumed, fragHeader.FragLength).CopyTo(fragBuffer.Span);

            if (!UdpSocket.Assembler.TryAssemble(header, fragHeader, fragBuffer, out var assembled))
                return;

            using (assembled)
            {
                var normalizedHeader = new UdpHeaderBuilder()
                    .From(header)
                    .WithPayloadLength(assembled.Count)
                    .Build();

                var payload = new ReadOnlyMemory<byte>(assembled.Array, 0, assembled.Count);
                var message = new UdpMessage(normalizedHeader, payload);
                OnReceivedMessage(message);
            }
        }
        else
        {
            var payload = new ReadOnlyMemory<byte>(buffer.Array, bodyOffset, header.PayloadLength);
            var message = new UdpMessage(header, payload);
            OnReceivedMessage(message);
        }
    }

    public virtual void Close(CloseReason reason)
    {
        _channelRouter.Unbind(ChannelKind.Reliable);
        _channelRouter.Unbind(ChannelKind.Unreliable);
        
        _receiveBuffer?.Dispose();
        _receiveBuffer = null;
        
        NetworkSession.Close(reason);
        UdpSocket?.Close(reason);
        StopFragmentAssemblerCleanupScheduler();
        CloseReason = reason;
    }

    public virtual void Initialize(IBaseEngine engine, TcpNetworkSession networkSession)
    {
        _engine = (BaseEngine)engine;
        _scheduler = engine.Scheduler;
        NetworkSession = networkSession;
        Id = networkSession.SessionId;
        _receiveBuffer = new PooledReceiveBuffer(engine.Config.Network.ReceiveBufferSize);
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
        catch (Exception ex)
        {
            Logger.Error(ex);
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
        _assemblerCleanupScheduler = _scheduler.Schedule(
            UdpSocket.Assembler.Cleanup,
            timeout,
            period,
            period);
    }

    private void StopFragmentAssemblerCleanupScheduler()
    {
        _assemblerCleanupScheduler?.Dispose();
    }

    private void EnsureUdpSocket(Socket socket, IPEndPoint remoteEndPoint)
    {
        lock (this)
        {
            if (UdpSocket == null)
            {
                UdpSocket = new UdpSocket(socket, remoteEndPoint);
                UdpSocket.Attach(this);
                _channelRouter.Bind(new UnreliableChannel(UdpSocket));
                StartFragmentAssemblerCleanupScheduler();
            }
            else
            {
                UdpSocket.UpdateRemoteEndPoint(remoteEndPoint);
            }
        }
    }
    
    void IBaseSession.ProcessBuffer(byte[] buffer, int offset, int length)
    {
        _receiveBuffer.Write(new ReadOnlySpan<byte>(buffer, offset, length));

        try
        {
            const int headerSize = TcpHeader.ByteSize;
            const int maxProcessPerTick = 150;
            var processedCount = 0;
            
            Span<byte> headerSpan = stackalloc byte[headerSize];

            while (_receiveBuffer.ReadableBytes >= headerSize)
            {
                if (processedCount++ >= maxProcessPerTick)
                    break;
                
                _receiveBuffer.Peek(headerSpan);
                
                if (!TcpHeader.TryRead(headerSpan, out var header, out var consumed))
                    break;

                // 페이로드 검증
                var bodyLen = header.PayloadLength;
                
                if (bodyLen < 0 || bodyLen > Config.Network.MaxFrameBytes)
                {
                    Logger.Warn("Invalid payload length. id={0}, max={1}, len={2}",
                        header.MsdId, Config.Network.MaxFrameBytes, bodyLen);
                    Close(CloseReason.ProtocolError);
                    break;
                }

                var totalLen = consumed + bodyLen;
                if (_receiveBuffer.ReadableBytes < totalLen)
                    break;

                // 헤더 소비
                _receiveBuffer.Consume(consumed);

                if (bodyLen > 0)
                {
                    using var pb = new PooledBuffer(bodyLen);
                    _receiveBuffer.Peek(pb.Span);
                    _receiveBuffer.Consume(bodyLen);
                    
                    var payload = new ReadOnlyMemory<byte>(pb.Array, 0, bodyLen);
                    var message = new TcpMessage(header, payload);
                    OnReceivedMessage(message);
                }
                else
                {
                    var message = new TcpMessage(header, ReadOnlyMemory<byte>.Empty);
                    OnReceivedMessage(message);
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
