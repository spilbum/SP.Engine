using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;

namespace SP.Sample.GameClient.Command;

[ProtocolCommand(G2CProtocol.GameEndNtf)]
public class GameEndNtf : BaseCommand<GameClient, G2CProtocolData.GameEndNtf>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.GameEndNtf protocol)
    {
        context.OnGameEnd(protocol.Rank, protocol.Reward);
    }
}
