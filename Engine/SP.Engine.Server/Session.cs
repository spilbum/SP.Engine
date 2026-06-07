using System;
using System.Threading;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Protocol;

namespace SP.Engine.Server;

public sealed class Session(long sessionId) : SessionBase(sessionId)
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

    public override void Initialize(EngineCore engine, TcpNetworkSession ns, ReadWriteBuffer readWriteBuffer)
    {
        base.Initialize(engine, ns, readWriteBuffer);
        Engine = (EngineBase)engine;
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

    protected override void OnUdpClosed(CloseReason reason)
    {
        base.OnUdpClosed(reason);
        SendUdpStatusNotify(false);
    }

    internal void SendUdpStatusNotify(bool enabled)
    {
        using var scope = ProtocolScope<S2CEngineProtocolData.UdpStatusNotify>.Rent();
        scope.Protocol.IsEnabled = enabled;
        InternalSend(scope.Protocol);
    }

    private void SendUdpHealthCheck()
    {
        _lastUdpCheckTimeTicks = DateTime.UtcNow.Ticks;
        using var scope = ProtocolScope<S2CEngineProtocolData.UdpHealthCheck>.Rent();
        InternalSend(scope.Protocol);
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
        SetupProtocolPolicy();
    }

    internal bool InternalSend<T>(T data) where T : class, IProtocolData, new()
    {
        IMessage message = null;
        try
        {
            var channel = data.Channel;
            if (channel == ChannelKind.Reliable
                || (channel == ChannelKind.Unreliable && !IsUdpAvailable))
            {
                message = MessagePool<TcpMessage>.Rent();
            }
            else
            {
                message = MessagePool<UdpMessage>.Rent();
            }
            
            message.Serialize(data, null, null, null);
            return TrySend(channel, message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return false;
        }
        finally
        {
            message?.Dispose();
        }
    }

    internal void SendPong(uint clientSendTimeMs)
    {
        using var scope = ProtocolScope<S2CEngineProtocolData.Pong>.Rent();
        scope.Protocol.ClientSendTimeMs = clientSendTimeMs;
        scope.Protocol.ServerTimeMs = EngineBase.NetworkTimeMs;  
        InternalSend(scope.Protocol);
    }

    internal void SendMessageAck(uint ackNumber)
    {
        using var scope = ProtocolScope<S2CEngineProtocolData.MessageAck>.Rent();
        scope.Protocol.AckNumber = ackNumber;
        InternalSend(scope.Protocol);
    }

    protected override void MessageReceived(IMessage message)
    {
        base.MessageReceived(message);
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
        using var scope = ProtocolScope<S2CEngineProtocolData.Close>.Rent();
        InternalSend(scope.Protocol);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _udpHealthCheckTimer?.Dispose();
        }
    }
}
