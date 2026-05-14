using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomRandomSearchReq)]
public class RoomRandomSearchReq : CommandBase<GamePeer, C2GProtocolData.RoomRandomSearchReq>
{
    protected override void ExecuteCommand(GamePeer context, C2GProtocolData.RoomRandomSearchReq protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
