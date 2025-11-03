using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;

namespace SP.Sample.GameClient.Command;

[ProtocolCommand(G2CProtocol.GameReadyNtf)]
public class GameReadyNtf : BaseCommand<GameClient, G2CProtocolData.GameReadyNtf>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.GameReadyNtf protocol)
    {
        context.Logger.Debug("GameReadyNtf received.");
        context.GameReadyCompletedNtf();
    }
}
