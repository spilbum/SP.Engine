using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.RoomCreateAck)]
public class RoomCreateAck : BaseCommand<Client, G2CProtocolData.RoomCreateAck>
{
    protected override Task ExecuteCommand(Client context, G2CProtocolData.RoomCreateAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RoomCreateAck failed: {0}", protocol.Result);
            return Task.CompletedTask;
        }

        context.Logger.Debug("Room created. roomId={0}, server={1}:{2}", protocol.RoomId, protocol.ServerIp,
            protocol.ServerPort);
        context.RoomJoinReq(protocol.RoomId);
        return Task.CompletedTask;
    }
}
