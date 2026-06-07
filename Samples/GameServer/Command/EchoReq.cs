using Common;
using GameServer.UserPeer;
using SP.Core;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.EchoReq)]
public class EchoReq : CommandBase<GamePeer, C2GProtocolData.EchoReq>
{
    protected override void ExecuteCommand(GamePeer context, C2GProtocolData.EchoReq protocol)
    {
        using var scope = ProtocolScope<G2CProtocolData.EchoAck>.Rent(context.Logger);
        scope.Protocol.Seq = protocol.Seq;
        scope.Protocol.SentTicks = protocol.SentTicks;
        context.Send(scope.Protocol);
    }
}
