using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.RankRangeReq)]
public class RankRangeReq : BaseCommand<GamePeer, C2GProtocolData.RankRangeReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.RankRangeReq protocol)
    {
        var connector = GameServer.Instance.GetRankConnector();
        if (connector == null)
        {
            context.Send(new G2CProtocolData.RankMyAck { Result = ErrorCode.InternalError });
            return;
        }

        connector.SearchRangeRank(context, protocol.SeasonKind, protocol.StartRank, protocol.Count);
    }
}
