using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomJoinReq)]
public class RoomJoinReq : BaseCommand<GamePeer, C2GProtocolData.RoomJoinReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.RoomJoinReq protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
