using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameEndNtf)]
public class GameEndNtf : BaseCommand<Client, G2CProtocolData.GameEndNtf>
{
    protected override Task ExecuteCommand(Client context, G2CProtocolData.GameEndNtf protocol)
    {
        context.OnGameEnd(protocol.Rank, protocol.Reward);
        return Task.CompletedTask;
    }
}
