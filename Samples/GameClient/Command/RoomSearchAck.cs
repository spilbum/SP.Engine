using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.RoomSearchAck)]
public class RoomSearchAck : BaseCommand<GameClient, G2CProtocolData.RoomSearchAck>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.RoomSearchAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RoomSearchAck failed: {0}", protocol.Result);
            return;
        }

        context.Logger.Debug("Room search completed. roomId={0}, server={1}:{2}",
            protocol.RoomId, protocol.ServerIp, protocol.ServerPort);
        context.RoomSearchReq(protocol.RoomId);
    }
}
