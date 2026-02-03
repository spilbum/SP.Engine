using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomRandomSearchReq)]
public class RoomRandomSearchReq : BaseCommand<GamePeer, C2GProtocolData.RoomRandomSearchReq>
{
    protected override Task ExecuteCommand(GamePeer context, C2GProtocolData.RoomRandomSearchReq protocol)
    {
        context.ExecuteProtocol(protocol);
        return Task.CompletedTask;
    }
}
