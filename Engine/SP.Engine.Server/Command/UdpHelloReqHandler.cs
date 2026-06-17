using System;
using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.C2S;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(ProtocolId.C2S.UdpHelloReq)]
internal class UdpHelloReqHandler : CommandHandlerBase<Session, UdpHelloReq>
{
    protected override void ExecuteCommand(Session session, UdpHelloReq protocol)
    {
        using var scope = ProtocolScope<UdpHelloAck>.Rent();

        try
        {
            if (!ValidateRequest(session, protocol, out var result))
            {
                scope.Protocol.Result = result;
                return;
            }

            var mtu = Math.Clamp(protocol.Mtu, session.Config.Network.UdpMinMtu, session.Config.Network.UdpMaxMtu);
            session.SetMaxFragmentSize(mtu);
            
            // 상태 체크 타이머 시작
            session.StartUdpHealthCheck();
            
            scope.Protocol.Mtu = mtu;
            scope.Protocol.Result = UdpHelloResult.Ok;
            session.Logger.Debug("Session {0} UDP handshake succeeded with MTU: {1}", session.SessionId, mtu);
        }
        catch (Exception ex)
        {
            scope.Protocol.Result = UdpHelloResult.InternalError;
            session.Logger.Error("Session {0} UDP handshake failed. err: {1}\n{2}", session.SessionId, ex.Message, ex.StackTrace);
        }
        finally
        {
            session.InternalSend(scope.Protocol);
        }
    }

    private static bool ValidateRequest(Session session, UdpHelloReq req, out UdpHelloResult result)
    {
        if (session.SessionId != req.SessionId || session.Peer?.PeerId != req.PeerId)
        {
            result = UdpHelloResult.InvalidRequest;
            return false;
        }

        if (session.IsClosing || session.IsClosed)
        {
            result = UdpHelloResult.SessionClosed;
            return false;
        }

        if (req.Mtu <= 0)
        {
            result = UdpHelloResult.InvalidRequest;
            return false;
        }

        result = UdpHelloResult.Ok;
        return true;
    }
}
