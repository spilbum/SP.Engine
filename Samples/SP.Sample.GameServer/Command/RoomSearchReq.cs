using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomSearchReq)]
public class RoomSearchReq : BaseCommand<GamePeer, C2GProtocolData.RoomSearchReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.RoomSearchReq protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
