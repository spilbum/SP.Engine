using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameActionAck)]
public class GameActionAck : BaseCommand<GameClient, G2CProtocolData.GameActionAck>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.GameActionAck protocol)
    {
        context.OnGameAction(protocol.Result, protocol.SeqNo);
    }
}
