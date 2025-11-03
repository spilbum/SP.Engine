using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.RoomLeaveNtf)]
public class RoomLeaveNtf : BaseCommand<GamePeer, C2GProtocolData.RoomLeaveNtf>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.RoomLeaveNtf protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
