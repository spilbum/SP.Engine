using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.GameActionReq)]
public class GameActionReq : BaseCommand<GamePeer, C2GProtocolData.GameActionReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.GameActionReq protocol)
    {
        context.ExecuteProtocol(protocol);
    }
}
