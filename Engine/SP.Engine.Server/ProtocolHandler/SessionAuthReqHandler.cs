using System;
using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Logging;
using SP.Engine.Server.S2S;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(ProtocolId.C2S.SessionAuthReq)]
internal class SessionAuthReqHandler : ProtocolHandlerBase<Session, SessionAuthReq>
{
    protected override void ExecuteProtocol(Session session, SessionAuthReq protocol)
    {
        using var scope = ProtocolScope<SessionAuthAck>.Rent();
        var engine = session.Engine;

        if (!session.TryEnterAuthenticating())
        {
            Logger.Warn($"Session {session.SessionId} attempted duplicate auth.");
            return;
        }
        
        try
        {
            var (result, peer) = protocol.SessionId == 0
                ? HandleNewSession(session, engine, protocol)
                : HandleReconnection(session, engine, protocol);

            scope.Protocol.Result = result;
            if (scope.Protocol.Result != SessionAuthResult.Ok) return;

            session.CompleteAuthenticated(peer);
            Logger.Debug($"Session {session.SessionId}({peer.PeerId}) TCP handshake succeeded.");
        }
        catch (Exception e)
        {
            scope.Protocol.Result = SessionAuthResult.InternalError;
            Logger.Error($"Session {session.SessionId} TCP handshake failed. err: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            if (scope.Protocol.Result == SessionAuthResult.Ok)
                scope.Protocol.FillSuccess(session, session.Peer);

            session.InternalSend(scope.Protocol);
        }
    }
    
    private static (SessionAuthResult, PeerBase) HandleNewSession(Session session, EngineBase engine, SessionAuthReq req)
    {
        if (session.Peer != null) return (SessionAuthResult.InvalidRequest, null);

        switch (req.PeerKind)
        {
            case PeerKind.User:
            {
                if (!engine.NewPeer(session, out var peer)) return (SessionAuthResult.InternalError, null);
                if (!peer.TryKeyExchange(req.EncryptKeySize, req.EncryptPublicKey))
                    return (SessionAuthResult.KeyExchangeFailed, null);
                
                return engine.JoinPeer(peer) 
                    ? (SessionAuthResult.Ok, peer) 
                    : (SessionAuthResult.InternalError, null);
            }
            case PeerKind.Server:
            {
                var peer = new S2SPendingPeer(session);
                return peer.TryKeyExchange(req.EncryptKeySize, req.EncryptPublicKey) 
                    ? (SessionAuthResult.Ok, peer)
                    : (SessionAuthResult.KeyExchangeFailed, null);
            }
            default:
                return (SessionAuthResult.InvalidRequest, null);
        }
    }

    private static (SessionAuthResult, PeerBase) HandleReconnection(Session session, EngineBase engine, SessionAuthReq req)
    {
        var prevSession = engine.GetSession(req.SessionId);
        PeerBase peer;
        
        if (prevSession != null)
        {
            if (prevSession.CloseReason == CloseReason.Rejected)
                return (SessionAuthResult.ReconnectionNotAllowed, null);
            
            peer = prevSession.Peer;
            
            prevSession.Peer = null;
            prevSession.Close(CloseReason.ServerClosing);
        }
        else
        {
            peer = engine.GetWaitingPeer(req.PeerId);
            if (peer == null) return (SessionAuthResult.PeerNotFound, null);
        }
        
        // 클라가 받은 시퀀스 번호로 갱신
        peer.MessageProcessor.AcknowledgeInFlight(req.NextExpectedSeq);
        
        return engine.ActivatePeer(peer, session) 
            ? (SessionAuthResult.Ok, targetPeer: peer)
            : (SessionAuthResult.InternalError, null);
    }
}

internal static class SessionAuthAckExtensions
{
    public static void FillSuccess(this SessionAuthAck ack, Session session, PeerBase peer)
    {
        var engine = session.Engine;
        
        ack.SessionId = session.SessionId;
        ack.PeerId = peer.PeerId;
        
        var config = session.Config.Network;
        ack.MaxPayloadLength = config.MaxPayloadLength;
        ack.ReliableInitialRetransmitTimeoutMs = config.ReliableInitialRetransmitTimeoutMs;
        ack.ReliableMaxRetransmitCount = config.ReliableMaxRetransmitCount;
        ack.ReliableMaxAckDelayMs = config.ReliableMaxAckDelayMs;
        ack.ReliableAckFrequency = config.ReliableAckFrequency;
        ack.ReliableMaxOutOfOrderCount = config.ReliableMaxOutOfOrderCount;
        ack.ReliableInFlightLimit = config.ReliableInFlightLimit;
        ack.ReliablePendingQueueCapacity = config.ReliablePendingQueueCapacity;
        
        ack.UdpOpenPort = engine.GetOpenPort(SocketMode.Udp);
        ack.FragmentAssemblerCleanupTimeoutSec = config.FragmentAssemblerCleanupTimeoutSec;
        ack.FragmentAssemblerPendingMessageThreshold = config.FragmentAssemblerPendingMessageThreshold;
        ack.FragmentAssemblerCleanupIntervalSec = config.FragmentAssemblerCleanupPeriodSec;
        
        if (config.UseEncrypt)
        {
            ack.UseEncrypt = true;
            ack.EncryptPublicKey = peer.LocalPublicKey;
        }

        if (config.UseCompress)
        {
            ack.UseCompress = true;
            ack.CompressionThreshold = config.CompressionThreshold;
        }

        ack.NextExpectedSeq = peer.MessageProcessor.NextExpectedSeq;
    }
}
