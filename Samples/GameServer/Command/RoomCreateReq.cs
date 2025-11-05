using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomCreateReq)]
public class RoomCreateReq : BaseCommand<GamePeer, C2GProtocolData.RoomCreateReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.RoomCreateReq protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
