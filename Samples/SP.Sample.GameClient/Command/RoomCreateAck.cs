using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;

namespace SP.Sample.GameClient.Command;

[ProtocolCommand(G2CProtocol.RoomCreateAck)]
public class RoomCreateAck : BaseCommand<GameClient, G2CProtocolData.RoomCreateAck>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.RoomCreateAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RoomCreateAck failed: {0}", protocol.Result);
            return;
        }

        context.Logger.Debug("Room created. roomId={0}, server={1}:{2}", protocol.RoomId, protocol.ServerIp,
            protocol.ServerPort);
        context.RoomJoinReq(protocol.RoomId);
    }
}
