using System;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;
using SP.Engine.Protocol;

namespace SP.Engine.Server.Handler;

[ProtocolHandler(C2SEngineProtocol.SessionAuthReq)]
internal class SessionAuth<TPeer> : BaseEngineHandler<Session<TPeer>, C2SEngineProtocolData.SessionAuthReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, C2SEngineProtocolData.SessionAuthReq protocol)
    {
        var engine = session.Engine;
        if (null == engine)
            return;

        var errorCode = EEngineErrorCode.Unknown;
        var peer = session.Peer;

        try
        {
            if (!string.IsNullOrEmpty(protocol.SessionId))
            {
                // 재연결
                var prevSession = engine.GetSession(protocol.SessionId);
                if (null != prevSession)
                {
                    // 이전 세션이 살아 있는 경우
                    if (ERejectReason.None != prevSession.RejectReason)
                        throw new InvalidOperationException(
                            $"Reconnection is not allowed because the session was rejected. sessionId={prevSession.SessionId} reason={prevSession.RejectReason}");

                    peer = prevSession.Peer;
                    prevSession.Close();

                    engine.OnlinePeer(peer, session);
                }
                else
                {
                    // 재 연결 대기인 경우
                    peer = engine.GetWaitingReconnectPeer(protocol.PeerId);
                    engine.OnlinePeer(peer, session);
                }
            }
            else
            {
                // 최조 연결
                if (null != peer)
                    return;

                peer = engine.CreatePeer(session, protocol.KeySize, protocol.ClientPublicKey);
                if (null == peer)
                    return;

                engine.JoinPeer(peer);
            }

            if (peer == null)
                throw new Exception("peer is null.");

            session.SetPeer(peer);
            session.SetAuthorized();
            errorCode = EEngineErrorCode.Success;
        }
        catch (Exception ex)
        {
            errorCode = EEngineErrorCode.Invalid;
            session.Logger.Error("Failed to authorize peer: exception={0}\r\nstackTrace={1}", ex.Message,
                ex.StackTrace);
        }
        finally
        {
            var authAck = new S2CEngineProtocolData.SessionAuthAck { ErrorCode = errorCode };
            if (errorCode == EEngineErrorCode.Success)
            {
                authAck.SessionId = session.SessionId;
                authAck.LimitRequestLength = session.Config.LimitRequestLength;
                authAck.SendTimeOutMs = session.Config.SendTimeOutMs;
                authAck.MaxReSendCnt = session.Config.MaxReSendCnt;

                if (null != peer)
                {
                    authAck.PeerId = peer.PeerId;
                    authAck.ServerPublicKey = peer.DhPublicKey;
                }
            }

            if (!session.TryInternalSend(authAck))
                session.Logger.Error("Failed to send auth ack. sessionId={0}", session.SessionId);
        }
    }
}
