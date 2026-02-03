using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameReadyNtf)]
public class GameReadyNtf : BaseCommand<Client, G2CProtocolData.GameReadyNtf>
{
    protected override Task ExecuteCommand(Client context, G2CProtocolData.GameReadyNtf protocol)
    {
        context.Logger.Debug("GameReadyNtf received.");
        context.GameReadyCompletedNtf();
        return Task.CompletedTask;
    }
}
