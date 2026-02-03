using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameStartNtf)]
public class GameStartNtf : BaseCommand<Client, G2CProtocolData.GameStartNtf>
{
    protected override Task ExecuteCommand(Client context, G2CProtocolData.GameStartNtf protocol)
    {
        context.OnGameStart();
        return Task.CompletedTask;
    }
}
