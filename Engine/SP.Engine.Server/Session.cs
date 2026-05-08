using System;
using System.Net;
using System.Threading;
using SP.Core.Logging;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public interface ISession
{
    long SessionId { get; }
    ILogger Logger { get; }
    IPEndPoint LocalEndPoint { get; }
    IPEndPoint RemoteEndPoint { get; }
    IEngineConfig Config { get; }
}

public sealed class Session : BaseSession, ISession
{
    private int _udpHealthFailCount;
    private BasePeer _peer;

    public BasePeer Peer
    {
        get => _peer;
        set => Interlocked.Exchange(ref _peer, value);
    }
    
    public Engine Engine { get; private set; }

    public override void Initialize(long sessionId, IBaseEngine baseEngine, TcpNetworkSession tcpSession)
    {
        base.Initialize(sessionId, baseEngine, tcpSession);
        Engine = (Engine)baseEngine;
    }

    internal void Authenticate(BasePeer peer)
    {
        Peer = peer;
        IsAuthenticated = true;
        peer.OnSessionAuthCompleted();
    }

    internal void SetupMaxFrameSize(ushort mtu)
    {
        _udpSession?.SetupMaxFrameSize(mtu);
    }

    internal void InvalidateUdpHealth(int maxFailCount)
    {
        var count = Interlocked.Increment(ref _udpHealthFailCount);
        if (count < maxFailCount) return;
        
        Interlocked.Exchange(ref _udpHealthFailCount, 0);
        if (!IsUdpAvailable) return;
        
        DisableUdp();
        Logger.Warn("Session {0} UDP disabled due to health check failure.", SessionId);
    }

    internal bool RecoverUdpHealth()
    {
        Interlocked.Exchange(ref _udpHealthFailCount, 0);
        if (IsUdpAvailable) return false;
        
        EnableUdp();
        Logger.Info("Session {0} UDP capability has been restored and enabled.", SessionId);
        return true;
    }

    internal bool InternalSend(IProtocolData data)
    {
        var policy = PolicyDefaults.InternalPolicy;
        var encryptor = policy.UseEncrypt ? Peer?.Encryptor : null;
        var compressor = policy.UseCompress ? Peer?.Compressor : null;
        var originalChannel = data.Channel;
        var channel = originalChannel == ChannelKind.Unreliable && !IsUdpAvailable
            ? ChannelKind.Reliable
            : originalChannel;

        switch (channel)
        {
            case ChannelKind.Reliable:
            {
                var tcp = new TcpMessage();
                using (tcp)
                {
                    tcp.Serialize(data, policy, encryptor, compressor);
                    return TrySend(channel, tcp);
                }
            }
            case ChannelKind.Unreliable:
            {
                var udp = new UdpMessage();
                using (udp)
                {
                    udp.SetSessionId(SessionId);
                    udp.Serialize(data, policy, encryptor, compressor);
                    return TrySend(channel, udp);
                }
            }
            default:
                throw new Exception($"Unknown channel: {channel}");
        }
    }

    internal void SendPong(uint clientSendTimeMs)
    {
        InternalSend(new S2CEngineProtocolData.Pong
        {
            ClientSendTimeMs = clientSendTimeMs,
            ServerTimeMs = Engine.NetworkTimeMs
        });
    }

    internal void SendMessageAck(uint ackNumber)
    {
        var messageAck = new S2CEngineProtocolData.MessageAck { AckNumber = ackNumber };
        InternalSend(messageAck);
    }

    protected override void MessageReceived(IMessage message)
    {
        base.MessageReceived(message);
        
        var peer = Peer;
        if (peer != null && message is TcpMessage { AckNumber: > 0 } tcp)
            peer.HandleRemoteAck(tcp.AckNumber);
            
        Engine.ExecuteCommand(this, message);
    }

    public override void Close(CloseReason reason)
    {
        if (reason is CloseReason.TimeOut or CloseReason.Rejected)
        {
            StartClosing();
            return;
        }
        
        base.Close(reason);
    }
    
    private void StartClosing()
    {
        if (Interlocked.CompareExchange(ref _state, (int)SessionState.Closing, (int)SessionState.Connected)
            != (int)SessionState.Connected)
        {
            return;
        }
        
        Logger.Debug("Session {0} start closing...", SessionId);
        
        StartClosingTime = DateTime.UtcNow;
        Engine.EnqueueCloseHandshakePending(this);
        SendClose();
    }
    
    internal void SendClose()
    {
        InternalSend(new S2CEngineProtocolData.Close());
    }
}
