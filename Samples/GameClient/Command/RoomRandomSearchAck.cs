using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.RoomRandomSearchAck)]
public class RoomRandomSearchAck : BaseCommand<Client, G2CProtocolData.RoomRandomSearchAck>
{
    protected override Task ExecuteCommand(Client context, G2CProtocolData.RoomRandomSearchAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RoomRandomSearchAck failed: {0}", protocol.Result);
            return Task.CompletedTask;
        }

        context.Logger.Debug("Room search completed. roomId={0}, server={1}:{2}",
            protocol.RoomId, protocol.ServerIp, protocol.ServerPort);

        context.RoomJoinReq(protocol.RoomId);
        return Task.CompletedTask;
    }
}
