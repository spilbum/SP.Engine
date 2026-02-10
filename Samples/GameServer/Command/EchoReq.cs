using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.EchoReq)]
public class EchoReq : BaseCommand<GamePeer, C2GProtocolData.EchoReq>
{
    protected override Task ExecuteCommand(GamePeer context, C2GProtocolData.EchoReq protocol)
    {
        //context.Logger.Debug($"EchoReq: {protocol.Message}");

        context.Send(new G2CProtocolData.EchoAck { Message = protocol.Message });
        return Task.CompletedTask;
    }
}
