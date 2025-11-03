using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.RankMyReq)]
public class RankMyReq : BaseCommand<GamePeer, C2GProtocolData.RankMyReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.RankMyReq protocol)
    {
        var connector = GameServer.Instance.GetRankConnector();
        if (connector == null)
        {
            context.Send(new G2CProtocolData.RankMyAck { Result = ErrorCode.InternalError });
            return;
        }

        connector.SearchMyRank(context, protocol.SeasonKind);
    }
}
