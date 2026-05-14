using System;
using System.Threading;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server;

public sealed class Session : SessionBase
{
    private long _lastUdpCheckTimeTicks;
    private int _udpHealthFailCount;
    private IDisposable _udpHealthCheckTimer;
    private PeerBase _peer;

    public PeerBase Peer
    {
        get => _peer;
        set => Interlocked.Exchange(ref _peer, value);
    }
    
    public EngineBase Engine { get; private set; }

    public override void Initialize(long sessionId, EngineCore engineCore, TcpNetworkSession tcpSession)
    {
        base.Initialize(sessionId, engineCore, tcpSession);
        Engine = (EngineBase)engineCore;
    }

    internal void StartUdpHealthCheck()
    {
        if (_udpHealthCheckTimer == null) return;
        _udpHealthCheckTimer = Engine.GlobalScheduler.Schedule(
            Engine.Fiber, 
            TickUdpHealthCheck, 
            TimeSpan.Zero, 
            TimeSpan.FromSeconds(Config.Session.UdpHealthCheckIntervalSec));
    }

    private void TickUdpHealthCheck()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var elapsedMs = (nowTicks - _lastUdpCheckTimeTicks) / TimeSpan.TicksPerMillisecond;
        var timeoutMs = Math.Max(Config.Session.UdpHealthCheckMinTimeoutMs, _peer.AvgRTTMs * 3);
        if (elapsedMs >= timeoutMs)
        {
            if (InvalidateUdpHealth(Config.Session.UdpHealthCheckMaxFailCount))
            {
                SendUdpStatusNotify(false);
                Logger.Warn("Session {0} UDP disabled.", SessionId);
            }
            
            SendUdpHealthCheck();
        }
    }

    internal void SendUdpStatusNotify(bool enabled)
    {
        InternalSend(new S2CEngineProtocolData.UdpStatusNotify { IsEnabled = enabled });
    }

    private void SendUdpHealthCheck()
    {
        _lastUdpCheckTimeTicks = DateTime.UtcNow.Ticks;
        InternalSend(new S2CEngineProtocolData.UdpHealthCheck());
    }

    private bool InvalidateUdpHealth(int maxFailCount)
    {
        var count = Interlocked.Increment(ref _udpHealthFailCount);
        if (count < maxFailCount || !IsUdpAvailable) return false;
        
        DisableUdp();
        return true;
    }

    internal bool RecoverUdpHealth()
    {
        if (IsUdpAvailable) return false;
        
        Interlocked.Exchange(ref _udpHealthFailCount, 0);
        _lastUdpCheckTimeTicks = DateTime.UtcNow.Ticks;
        
        EnableUdp();
        return true;
    }
    
    internal void Authenticate(PeerBase peer)
    {
        Peer = peer;
        IsAuthenticated = true;
        peer.OnSessionAuthCompleted();
    }

    internal void SetupFrameSize(ushort mtu)
    {
        _udpSession?.SetupFrameSize(mtu);
    }

    internal bool InternalSend(IProtocolData data)
    {
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
                    tcp.Serialize(data);
                    return Send(channel, tcp);
                }
            }
            case ChannelKind.Unreliable:
            {
                var udp = new UdpMessage();
                using (udp)
                {
                    udp.Serialize(data);
                    return Send(channel, udp);
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
            ServerTimeMs = EngineBase.NetworkTimeMs
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
        
        _udpHealthCheckTimer?.Dispose();
        base.Close(reason);
    }
    
    private void StartClosing()
    {
        if (Interlocked.CompareExchange(ref _state, (int)SessionState.Closing, (int)SessionState.Connected)
            != (int)SessionState.Connected)
        {
            return;
        }
        
        StartClosingTime = DateTime.UtcNow;
        Engine.EnqueueCloseHandshakePending(this);
        SendClose();
    }
    
    internal void SendClose()
    {
        InternalSend(new S2CEngineProtocolData.Close());
    }
}
