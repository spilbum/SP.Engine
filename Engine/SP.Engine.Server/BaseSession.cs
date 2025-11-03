using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
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
    void ProcessDatagram(byte[] datagram, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint);
    void Close(CloseReason reason);
}

public abstract class BaseSession : IBaseSession
{
    private readonly MessageChannelRouter _channelRouter = new();
    private IDisposable _assemblerCleanupScheduler;
    private BaseEngine _engine;
    private BinaryReceiveBuffer _receiveBuffer;
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

    void IBaseSession.ProcessDatagram(byte[] datagram, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint)
    {
        EnsureUdpSocket(socket, remoteEndPoint);

        var bodyOffset = header.Size;
        var bodyLen = header.PayloadLength;

        if (header.Fragmented == 0x01)
        {
            var bodySpan = new ReadOnlySpan<byte>(datagram, bodyOffset, bodyLen);
            if (!FragmentHeader.TryParse(bodySpan, out var fragHeader, out var consumed))
                return;

            if (bodyLen < consumed + fragHeader.FragLength)
                return;

            var fragPayload = new ArraySegment<byte>(
                datagram, bodyOffset + consumed, fragHeader.FragLength);

            if (!UdpSocket.Assembler.TryAssemble(header, fragHeader, fragPayload, out var assembled))
                return;

            var normalizedHeader = new UdpHeaderBuilder()
                .From(header)
                .WithPayloadLength(assembled.Count)
                .Build();

            var msg = new UdpMessage(normalizedHeader, assembled);
            OnReceivedMessage(msg);
        }
        else
        {
            var payload = new ArraySegment<byte>(datagram, bodyOffset, bodyLen);
            var msg = new UdpMessage(header, payload);
            OnReceivedMessage(msg);
        }
    }

    void IBaseSession.ProcessBuffer(byte[] buffer, int offset, int length)
    {
        _receiveBuffer.TryWrite(buffer, offset, length);

        try
        {
            foreach (var message in Filter())
                OnReceivedMessage(message);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
        finally
        {
            _receiveBuffer.ResetIfConsumed();
        }
    }

    public virtual void Close(CloseReason reason)
    {
        _channelRouter.Unbind(ChannelKind.Reliable);
        _channelRouter.Unbind(ChannelKind.Unreliable);
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
        _receiveBuffer = new BinaryReceiveBuffer(engine.Config.Network.ReceiveBufferSize);
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

    private IEnumerable<TcpMessage> Filter()
    {
        const int maxFramePerTick = 128;
        const int headerSize = TcpHeader.ByteSize;
        var frames = 0;

        while (frames < maxFramePerTick)
        {
            if (_receiveBuffer.ReadableBytes < headerSize)
                yield break;

            var headerSpan = _receiveBuffer.ReadableSpan[..headerSize];
            if (!TcpHeader.TryRead(headerSpan, out var header, out var consumed))
                yield break;

            var bodyLen = header.PayloadLength;
            if (bodyLen <= 0 || bodyLen > Config.Network.MaxFrameBytes)
            {
                Logger.Warn("Invalid payload length. id={0}, max={1}, len={2}",
                    header.MsdId, Config.Network.MaxFrameBytes, bodyLen);
                Close(CloseReason.ProtocolError);
                yield break;
            }

            var total = consumed + bodyLen;
            if (_receiveBuffer.ReadableBytes < total)
                yield break;

            _receiveBuffer.Consume(consumed);

            if (!_receiveBuffer.TryReadMemory(bodyLen, out var bodyMem))
                yield break;

            yield return new TcpMessage(header, bodyMem);
            frames++;
        }
    }

    protected abstract void ExecuteMessage(IMessage message);

    public virtual void Close()
    {
        Close(CloseReason.ServerClosing);
    }
}
