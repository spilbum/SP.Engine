using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.RoomMemberLeaveNtf)]
public class RoomMemberLeaveNtf : BaseCommand<NetworkClient, G2CProtocolData.RoomMemberLeaveNtf>
{
    protected override void ExecuteProtocol(NetworkClient context, G2CProtocolData.RoomMemberLeaveNtf protocol)
    {
        context.Logger.Debug("Room member leaved. roomId={0}, member={1}, reason={2}, memberCnt={3}",
            protocol.RoomId, protocol.Uid, protocol.Reason, protocol.RoomMemberCount);
    }
}
