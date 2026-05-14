using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.GameEndNtf)]
public class GameEndNtf : CommandBase<Client, G2CProtocolData.GameEndNtf>
{
    protected override void ExecuteCommand(Client context, G2CProtocolData.GameEndNtf protocol)
    {
        context.OnGameEnd(protocol.Rank, protocol.Reward);
    }
}
