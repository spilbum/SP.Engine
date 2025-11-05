using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameStartNtf)]
public class GameStartNtf : BaseCommand<GameClient, G2CProtocolData.GameStartNtf>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.GameStartNtf protocol)
    {
        context.OnGameStart();
    }
}
