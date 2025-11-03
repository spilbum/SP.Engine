using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomRandomSearchReq)]
public class RoomRandomSearchReq : BaseCommand<GamePeer, C2GProtocolData.RoomRandomSearchReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.RoomRandomSearchReq protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
