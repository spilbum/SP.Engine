using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.UdpEchoReq)]
public class UdpEchoReq : CommandBase<GamePeer, C2GProtocolData.UdpEchoReq>
{
    protected override void ExecuteCommand(GamePeer context, C2GProtocolData.UdpEchoReq protocol)
    {
        using var scope = ProtocolScope<G2CProtocolData.UdpEchoAck>.Rent(context.Logger);
        scope.Protocol.Seq = protocol.Seq;
        scope.Protocol.SentTicks = protocol.SentTicks;
        scope.Protocol.Data = protocol.Data;
        context.Send(scope.Protocol);
    }
}
