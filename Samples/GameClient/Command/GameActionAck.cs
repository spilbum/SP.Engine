using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameActionAck)]
public class GameActionAck : CommandBase<Client, G2CProtocolData.GameActionAck>
{
    protected override void ExecuteCommand(Client context, G2CProtocolData.GameActionAck protocol)
    {
        context.OnGameAction(protocol.Result, protocol.SeqNo);
    }
}
