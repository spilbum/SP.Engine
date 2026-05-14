using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameStartNtf)]
public class GameStartNtf : CommandBase<Client, G2CProtocolData.GameStartNtf>
{
    protected override void ExecuteCommand(Client context, G2CProtocolData.GameStartNtf protocol)
    {
        context.OnGameStart();
    }
}
