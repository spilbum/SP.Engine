using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Protocol;

namespace SP.Engine.Server.Command;

[ProtocolCommand(C2SEngineProtocolId.UdpHelloReq)]
internal class UdpHelloReq : CommandBase<Session, C2SEngineProtocolData.UdpHelloReq>
{
    protected override void ExecuteCommand(Session session, C2SEngineProtocolData.UdpHelloReq protocol)
    {
        using var scope = ProtocolScope<S2CEngineProtocolData.UdpHelloAck>.Rent();

        try
        {
            if (!ValidateRequest(session, protocol, out var result))
            {
                scope.Protocol.Result = result;
                return;
            }

            var mtu = NegotiateMtu(session.Config.Network, protocol.Mtu);
            session.SetMaxFragmentSize(mtu);
            
            // 상태 체크 타이머 시작
            session.StartUdpHealthCheck();
            
            scope.Protocol.Mtu = mtu;
            scope.Protocol.Result = UdpHandshakeResult.Ok;
            session.Logger.Debug("Session {0} UDP handshake succeeded with MTU: {1}", session.SessionId, mtu);
        }
        catch (Exception ex)
        {
            scope.Protocol.Result = UdpHandshakeResult.InternalError;
            session.Logger.Error("Session {0} UDP handshake failed. err: {1}\n{2}", session.SessionId, ex.Message, ex.StackTrace);
        }
        finally
        {
            session.InternalSend(scope.Protocol);
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
