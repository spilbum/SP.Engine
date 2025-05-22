using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(EngineProtocol.C2S.SessionAuthReq)]
internal class SessionAuth<TPeer> : BaseEngineHandler<Session<TPeer>, EngineProtocolData.C2S.SessionAuthReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(Session<TPeer> session, EngineProtocolData.C2S.SessionAuthReq protocol)
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
                        throw new ErrorCodeException(EEngineErrorCode.ReconnectionFailed,
                            $"Reconnection is not allowed because the session was rejected. sessionId={prevSession.SessionId} reason={prevSession.RejectReason}");

                    peer = prevSession.Peer;
                    prevSession.Close();
                }
                else
                {
                    // 재 연결 대기인 경우
                    peer = engine.GetWaitingReconnectPeer(protocol.PeerId);
                    if (peer == null)
                        throw new ErrorCodeException(EEngineErrorCode.ReconnectionFailed,
                            $"No waiting reconnection peer found for sessionId={protocol.SessionId}");
                }

                engine.OnlinePeer(peer, session);
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
        catch (ErrorCodeException e)
        {
            errorCode = e.ErrorCode;
            session.Logger.Error("errorCode={0}, exception={1}\r\nstackTrace={2}", e.ErrorCode, e.Message,
                e.StackTrace);
        }
        catch (Exception ex)
        {
            errorCode = EEngineErrorCode.Invalid;
            session.Logger.Error("exception={0}\r\nstackTrace={1}", ex.Message,
                ex.StackTrace);
        }
        finally
        {
            var authAck = new EngineProtocolData.S2C.SessionAuthAck { ErrorCode = errorCode };
            if (errorCode == EEngineErrorCode.Success)
            {
                authAck.SessionId = session.SessionId;
                authAck.MaxAllowedLength = session.Config.MaxAllowedLength;
                authAck.SendTimeOutMs = session.Config.SendTimeOutMs;
                authAck.MaxReSendCnt = session.Config.MaxReSendCnt;

                if (null != peer)
                {
                    authAck.PeerId = peer.PeerId;
                    
                    if (session.Config.UseEncryption)
                    {
                        authAck.UseEncryption = true;
                        authAck.ServerPublicKey = peer.DhPublicKey;
                    }

                    if (session.Config.UseCompression)
                    {
                        authAck.UseCompression = true;
                        authAck.CompressionThresholdPercent = session.Config.CompressionThresholdPercent;
                    }
                }
            }

            if (!session.TryInternalSend(authAck))
                session.Logger.Error("Failed to send session auth ack. sessionId={0}", session.SessionId);
        }
    }
}
