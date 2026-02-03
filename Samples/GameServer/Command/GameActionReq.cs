using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.GameActionReq)]
public class GameActionReq : BaseCommand<GamePeer, C2GProtocolData.GameActionReq>
{
    protected override Task ExecuteCommand(GamePeer context, C2GProtocolData.GameActionReq protocol)
    {
        context.ExecuteProtocol(protocol);
        return Task.CompletedTask;
    }
}
