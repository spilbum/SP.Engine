using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.RoomMemberEnterNtf)]
public class RoomMemberEnterNtf : BaseCommand<NetworkClient, G2CProtocolData.RoomMemberEnterNtf>
{
    protected override void ExecuteProtocol(NetworkClient context, G2CProtocolData.RoomMemberEnterNtf protocol)
    {
        context.Logger.Debug("Room member leaved. roomId={0}, member={1} ({2}), memberCnt={3}",
            protocol.RoomId, protocol.Member?.Name, protocol.Member?.UserId, protocol.RoomMemberCount);
    }
}
