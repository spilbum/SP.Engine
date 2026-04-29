using System;
using System.Net;
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
    bool TrySend(ChannelKind channel, IMessage message);
}

public sealed class Session : BaseSession, ISession
{
    private int _udpHealthFailCount;
    private Engine _engine;
    public BasePeer Peer { get; internal set; }

    public override void Initialize(long sessionId, IBaseEngine baseEngine, TcpNetworkSession tcpSession)
    {
        base.Initialize(sessionId, baseEngine, tcpSession);
        _engine = (Engine)baseEngine;
    }

    internal void OnSessionHandshake(C2SEngineProtocolData.SessionAuthReq req)
    {
        var ack = new S2CEngineProtocolData.SessionAuthAck { Result = SessionAuthResult.None };
        var engine = _engine;
        var peer = Peer;

        try
        {
            if (req.SessionId == 0)
            {
                // 최초 연결
                if (null != peer)
                    throw new SessionAuthException(SessionAuthResult.InvalidRequest,
                        $"Already created peer. sessionId={peer.Session.SessionId}, peerId={peer.PeerId}");

                if (!engine.NewPeer(this, out peer))
                    throw new SessionAuthException(SessionAuthResult.InternalError, $"Failed to create peer. sessionId={SessionId}");

                if (!peer.TryKeyExchange(req.KeySize, req.ClientPublicKey))
                    throw new SessionAuthException(SessionAuthResult.KeyExchangeFailed, "Key exchange failed.");

                engine.JoinPeer(peer);
            }
            else
            {
                // 재연결
                var prevSession = engine.GetSession(req.SessionId);
                if (null != prevSession)
                {
                    // 이전 세션이 살아 있는 경우
                    if (CloseReason.Rejected == prevSession.CloseReason)
                        throw new SessionAuthException(SessionAuthResult.ReconnectionNotAllowed,
                            $"Reconnection is not allowed because the session was rejected. sessionId={prevSession.SessionId}");

                    peer = prevSession.Peer;
                    prevSession.Close(CloseReason.ServerClosing);
                }
                else
                {
                    // 재 연결 대기 피어 조회
                    peer = engine.GetWaitingReconnectPeer(req.PeerId);
                    if (peer == null)
                        return;
                }

                if (!engine.OnlinePeer(peer, this))
                    return;
                
                Peer = peer;
            }

            if (peer == null) throw new InvalidOperationException("peer is null.");
            
            IsAuthenticated = true;
            peer.OnSessionAuthCompleted();
            
            ack.Result = SessionAuthResult.Ok;
            Logger.Debug("Session authentication succeeded. sessionId={0}, peerId={1}", SessionId, peer.PeerId);
        }
        catch (SessionAuthException e)
        {
            ack.Result = e.Result;
#if DEBUG
            ack.Reason = e.Message;
#endif
            Logger.Error("Session authentication failed ({0}). {1}\r\nstackTrace={2}",
                e.Result, e.Message, e.StackTrace);
        }
        catch (Exception e)
        {
            ack.Result = SessionAuthResult.InternalError;
            Logger.Error("Session authentication failed. {0}\r\nstackTrace={1}",
                e.Message, e.StackTrace);
        }
        finally
        {
            if (ack.Result == SessionAuthResult.Ok)
            {
                var network = Config.Network;
                ack.SessionId = SessionId;
                ack.MaxFrameBytes = network.MaxFrameBytes;
                ack.SendTimeoutMs = network.SendTimeoutMs;
                ack.MaxRetries = network.MaxRetransmissionCount;
                ack.MaxAckDelayMs = network.MaxAckDelayMs;
                ack.AckStepThreshold = network.AckStepThreshold;
                ack.UdpOpenPort = engine.GetOpenPort(SocketMode.Udp);
                ack.UdpAssemblyTimeoutSec = network.UdpAssemblyTimeoutSec;
                ack.UdpMaxPendingMessageCount = network.UdpMaxPendingMessageCount;
                ack.UdpCleanupIntervalSec = network.UdpCleanupIntervalSec;
                ack.MaxOutOfOrderCount = network.MaxOutOfOderCount;

                if (peer != null)
                {
                    ack.PeerId = peer.PeerId;

                    if (network.UseEncrypt)
                    {
                        ack.UseEncrypt = true;
                        ack.ServerPublicKey = peer.LocalPublicKey;
                    }

                    if (network.UseCompress)
                    {
                        ack.UseCompress = true;
                        ack.CompressionThreshold = network.CompressionThreshold;
                    }
                }
            }

            if (!InternalSend(ack))
                Logger.Error("Failed to send session auth ack. sessionId={0}", SessionId);
        }
    }

    internal void OnUdpHandshake(C2SEngineProtocolData.UdpHelloReq req)
    {
        var ack = new S2CEngineProtocolData.UdpHelloAck { Result = UdpHandshakeResult.None };

        try
        {
            if (SessionId != req.SessionId)
            {
                ack.Result = UdpHandshakeResult.InvalidRequest;
                Logger.Debug("UDP Hello: SessionId mismatch. Expected={0}, Received={1}", SessionId, req.SessionId);
                return;
            }

            if (Peer == null || Peer.PeerId != req.PeerId)
            {
                ack.Result = UdpHandshakeResult.InvalidRequest;
                Logger.Debug("UDP Hello: PeerId mismatch or null. sessionId={0}", SessionId);
                return;
            }

            if (IsClosing || IsClosed)
            {
                ack.Result = UdpHandshakeResult.InvalidRequest;
                Logger.Debug("UDP hello while closing. sid={0}", SessionId);
                return;
            }

            if (req.Mtu <= 0)
            {
                ack.Result = UdpHandshakeResult.InvalidRequest;
                Logger.Debug("UDP hello invalid mtu. mtu={0}", req.Mtu);
                return;
            }

            const ushort minIpv4Mtu = 576;

            var net = Config.Network;
            var minMtu = Math.Max(minIpv4Mtu, net.UdpMinMtu);
            var maxMtu = Math.Max(minMtu, net.UdpMaxMtu);
            var negotiated = Math.Min(Math.Max(req.Mtu, minMtu), maxMtu);
            SetMtu(negotiated);
            ack.Mtu = negotiated;

            Logger.Debug("UDP hello OK - pid={0}, sid={1}, mtu={2}", Peer.PeerId, SessionId, negotiated);
            ack.Result = UdpHandshakeResult.Ok;
        }
        catch (Exception e)
        {
            ack.Result = UdpHandshakeResult.InternalError;
            Logger.Error("Udp handshake failed. {0}", e.Message);
        }
        finally
        {
            if (!InternalSend(ack))
                Logger.Error("Failed to send UDP hello ack. sessionId={0}", SessionId);
        }
    }

    internal void OnUdpHealthCheckReq()
    {
        _udpHealthFailCount++;
        if (_udpHealthFailCount > Config.Network.MaxUdpHealthFail)
        {
            _udpHealthFailCount = 0;
            DisableUdp();
            Logger.Warn("UDP disabled for session {0} due to health check failure.", SessionId);
        }

        InternalSend(new S2CEngineProtocolData.UdpHealthCheckAck());
    }

    internal void OnUdpHealthCheckConfirm()
    {
        _udpHealthFailCount = 0;
        if (IsUdpAvailable) return;
        EnableUdp();
        Logger.Info("UDP restored for session {0}.", SessionId);
    }

    private bool InternalSend(IProtocolData data)
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

    internal void SendCloseHandshake()
    {
        if (IsClosing) return;
        IsClosing = true;
        StartClosingTime = DateTime.UtcNow;
        _engine.EnqueueCloseHandshakePending(this);

        var close = new S2CEngineProtocolData.Close();
        InternalSend(close);
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
            
        _engine.ExecuteCommand(this, message);
    }

    public override void Close(CloseReason reason)
    {
        if (reason is CloseReason.TimeOut or CloseReason.Rejected)
        {
            SendCloseHandshake();
            return;
        }

        base.Close(reason);
    }
}
