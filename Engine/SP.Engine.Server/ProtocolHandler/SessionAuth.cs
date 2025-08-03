using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;

namespace SP.Engine.Server.ProtocolHandler;

[ProtocolHandler(EngineProtocol.C2S.SessionAuthReq)]
internal class SessionAuth<TPeer> : BaseEngineHandler<ClientSession<TPeer>, EngineProtocolData.C2S.SessionAuthReq>
    where TPeer : BasePeer, IPeer
{
    protected override void ExecuteProtocol(ClientSession<TPeer> session, EngineProtocolData.C2S.SessionAuthReq protocol)
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
                var prevSession = (ClientSession<TPeer>)engine.GetSession(protocol.SessionId);
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

                peer = engine.CreateNewPeer(session, protocol.KeySize, protocol.ClientPublicKey);
                if (null == peer)
                    return;

                engine.JoinPeer(peer);
            }

            if (peer == null)
                throw new Exception("peer is null.");

            peer.SetUdpMtu(protocol.UdpMtu);
            session.SetPeer(peer);
            session.SetAuthorized();
            errorCode = EEngineErrorCode.Success;
            session.Logger.Debug("Session auth succeeded: {0}({1})", peer.PeerId, session.SessionId);
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
            var sessionAuthAck = new EngineProtocolData.S2C.SessionAuthAck { ErrorCode = errorCode };
            if (errorCode == EEngineErrorCode.Success)
            {
                sessionAuthAck.SessionId = session.SessionId;
                sessionAuthAck.MaxAllowedLength = session.Config.MaxAllowedLength;
                sessionAuthAck.SendTimeOutMs = session.Config.SendTimeOutMs;
                sessionAuthAck.MaxReSendCnt = session.Config.MaxReSendCnt;
                sessionAuthAck.UdpOpenPort = session.Engine.GetOpenPort(ESocketMode.Udp);
                
                if (null != peer)
                {
                    sessionAuthAck.PeerId = peer.PeerId;
                    
                    if (session.Config.UseEncryption)
                    {
                        sessionAuthAck.UseEncryption = true;
                        sessionAuthAck.ServerPublicKey = peer.DhPublicKey;
                    }

                    if (session.Config.UseCompression)
                    {
                        sessionAuthAck.UseCompression = true;
                        sessionAuthAck.CompressionThresholdPercent = session.Config.CompressionThresholdPercent;
                    }
                }
            }

            if (!session.Send(sessionAuthAck))
                session.Logger.Error("Failed to send session auth ack. sessionId={0}", session.SessionId);
        }
    }
}
