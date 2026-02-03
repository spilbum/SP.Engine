using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomJoinReq)]
public class RoomJoinReq : BaseCommand<GamePeer, C2GProtocolData.RoomJoinReq>
{
    protected override Task ExecuteCommand(GamePeer context, C2GProtocolData.RoomJoinReq protocol)
    {
        context.ExecuteProtocol(protocol);
        return Task.CompletedTask;
    }
}
