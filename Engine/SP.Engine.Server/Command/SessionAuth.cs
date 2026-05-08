using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.SessionAuthReq)]
internal class SessionAuth : BaseCommand<Session, C2SEngineProtocolData.SessionAuthReq>
{
    protected override void ExecuteCommand(Session session, C2SEngineProtocolData.SessionAuthReq protocol)
    {
        var ack = new S2CEngineProtocolData.SessionAuthAck { Result = SessionAuthResult.None };
        var engine = session.Engine;
        
        try
        {
            var (result, peer) = protocol.SessionId == 0
                ? HandleNewSession(session, engine, protocol)
                : HandleReconnection(session, engine, protocol);

            ack.Result = result;
            if (ack.Result != SessionAuthResult.Ok) return;

            session.Authenticate(peer);
            session.Logger.Debug("Session {0}({1}) TCP handshake succeeded.", session.SessionId, peer.PeerId);
        }
        catch (Exception e)
        {
            ack.Result = SessionAuthResult.InternalError;
            session.Logger.Error("Session {0} TCP handshake failed. err: {1}\n{2}", session.SessionId, e.Message, e.StackTrace);
        }
        finally
        {
            if (ack.Result == SessionAuthResult.Ok)
                ack.FillSuccess(session, session.Peer);

            if (!session.InternalSend(ack))
            {
                session.Logger.Warn("Failed to send TCP handshake: sessionId={0}", session.SessionId);
            }
        }
    }
    
    private static (SessionAuthResult, BasePeer) HandleNewSession(Session session, Engine engine, C2SEngineProtocolData.SessionAuthReq req)
    {
        if (session.Peer != null) return (SessionAuthResult.InvalidRequest, null);

        if (!engine.NewPeer(session, out var peer)) return (SessionAuthResult.InternalError, null);
        
        if (!peer.TryKeyExchange(req.KeySize, req.ClientPublicKey))
            return (SessionAuthResult.KeyExchangeFailed, null);

        engine.JoinPeer(peer);
        return (SessionAuthResult.Ok, peer);
    }

    private static (SessionAuthResult, BasePeer) HandleReconnection(Session session, Engine engine, C2SEngineProtocolData.SessionAuthReq req)
    {
        var prevSession = engine.GetSession(req.SessionId);
        BasePeer targetPeer;
        
        if (prevSession != null)
        {
            if (prevSession.CloseReason == CloseReason.Rejected)
                return (SessionAuthResult.ReconnectionNotAllowed, null);
            
            targetPeer = prevSession.Peer;
            prevSession.Close(CloseReason.ServerClosing);
        }
        else
        {
            targetPeer = engine.GetWaitingPeer(req.PeerId);
            if (targetPeer == null) return (SessionAuthResult.PeerNotFound, null);
        }
        
        return engine.OnlinePeer(targetPeer, session) 
            ? (SessionAuthResult.Ok, targetPeer)
            : (SessionAuthResult.InternalError, null);
    }
}

public static class SessionAuthAckExtensions
{
    public static void FillSuccess(this S2CEngineProtocolData.SessionAuthAck ack, Session session, BasePeer peer)
    {
        var net = session.Config.Network;
        var engine = session.Engine;
        
        ack.SessionId = session.SessionId;
        ack.PeerId = peer.PeerId;
        
        ack.MaxFrameBytes = net.MaxFrameBytes;
        ack.SendTimeoutMs = net.SendTimeoutMs;
        ack.MaxRetries = net.MaxRetransmissionCount;
        ack.MaxAckDelayMs = net.MaxAckDelayMs;
        ack.AckStepThreshold = net.AckStepThreshold;
        ack.MaxOutOfOrderCount = net.MaxOutOfOderCount;
        
        ack.UdpOpenPort = engine.GetOpenPort(SocketMode.Udp);
        ack.UdpAssemblyTimeoutSec = net.UdpAssemblyTimeoutSec;
        ack.UdpMaxPendingMessageCount = net.UdpMaxPendingMessageCount;
        ack.UdpCleanupIntervalSec = net.UdpCleanupIntervalSec;
        
        if (net.UseEncrypt)
        {
            ack.UseEncrypt = true;
            ack.ServerPublicKey = peer.LocalPublicKey;
        }

        if (net.UseCompress)
        {
            ack.UseCompress = true;
            ack.CompressionThreshold = net.CompressionThreshold;
        }
    }
}
