using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpHelloReq)]
internal class UdpHelloReq : CommandBase<Session, C2SEngineProtocolData.UdpHelloReq>
{
    protected override void ExecuteCommand(Session session, C2SEngineProtocolData.UdpHelloReq protocol)
    {
        var ack = new S2CEngineProtocolData.UdpHelloAck { Result = UdpHandshakeResult.None };

        try
        {
            if (!ValidateRequest(session, protocol, out var result))
            {
                ack.Result = result;
                return;
            }

            var mtu = NegotiateMtu(session.Config.Network, protocol.Mtu);
            session.SetupFrameSize(mtu);
            
            // 상태 체크 타이머 시작
            session.StartUdpHealthCheck();
            
            ack.Mtu = mtu;
            ack.Result = UdpHandshakeResult.Ok;
            session.Logger.Debug("Session {0} UDP handshake succeeded with MTU: {1}", session.SessionId, mtu);
        }
        catch (Exception ex)
        {
            ack.Result = UdpHandshakeResult.InternalError;
            session.Logger.Error("Session {0} UDP handshake failed. err: {1}\n{2}", session.SessionId, ex.Message, ex.StackTrace);
        }
        finally
        {
            if (!session.InternalSend(ack))
            {
                session.Logger.Warn("Failed to send UDP handshake: sessionId={0}", session.SessionId);
            }
        }
    }

    private static bool ValidateRequest(Session session, C2SEngineProtocolData.UdpHelloReq req, out UdpHandshakeResult result)
    {
        if (session.SessionId != req.SessionId || session.Peer?.PeerId != req.PeerId)
        {
            result = UdpHandshakeResult.InvalidRequest;
            return false;
        }

        if (session.IsClosing || session.IsClosed)
        {
            result = UdpHandshakeResult.SessionClosed;
            return false;
        }

        if (req.Mtu <= 0)
        {
            result = UdpHandshakeResult.InvalidRequest;
            return false;
        }

        result = UdpHandshakeResult.Ok;
        return true;
    }

    private static ushort NegotiateMtu(NetworkConfig config, ushort clientMtu)
    {
        var minMtu = Math.Max(config.UdpMinMtu, clientMtu);
        var maxMtu = Math.Max(minMtu, config.UdpMaxMtu);
        return Math.Min(Math.Max(clientMtu, minMtu), maxMtu);
    }
}
