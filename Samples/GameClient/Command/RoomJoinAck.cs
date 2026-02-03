using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.RoomJoinAck)]
public class RoomJoinAck : BaseCommand<Client, G2CProtocolData.RoomJoinAck>
{
    protected override Task ExecuteCommand(Client context, G2CProtocolData.RoomJoinAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RoomJoin failed. {0}", protocol.Result);
            return Task.CompletedTask;
        }

        context.Logger.Debug("Room joined. kind={0}, roomId={1}, members=[{2}]",
            protocol.RoomKind, protocol.RoomId,
            string.Join(", ", protocol.Members!.Select(m => $"{m.Name} ({m.UserId})")));
        return Task.CompletedTask;
    }
}
