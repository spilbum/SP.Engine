using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameStartNtf)]
public class GameStartNtf : BaseCommand<NetworkClient, G2CProtocolData.GameStartNtf>
{
    protected override void ExecuteProtocol(NetworkClient context, G2CProtocolData.GameStartNtf protocol)
    {
        context.OnGameStart();
    }
}
