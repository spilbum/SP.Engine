using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.GameReadyCompletedNtf)]
public class GameReadyCompletedNtf : BaseCommand<GamePeer, C2GProtocolData.GameReadyCompletedNtf>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.GameReadyCompletedNtf protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
