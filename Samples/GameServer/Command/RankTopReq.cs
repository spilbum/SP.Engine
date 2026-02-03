using Common;
using GameServer.UserPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Command;

[ProtocolCommand(C2GProtocol.RankTopReq)]
public class RankTopReq : BaseCommand<GamePeer, C2GProtocolData.RankTopReq>
{
    protected override Task ExecuteCommand(GamePeer context, C2GProtocolData.RankTopReq protocol)
    {
        var connector = GameServer.Instance.GetRankConnector();
        if (connector == null)
        {
            context.Send(new G2CProtocolData.RankTopAck { Result = ErrorCode.InternalError });
            return Task.CompletedTask;
        }

        connector.SearchTopRank(context, protocol.SeasonKind, protocol.Count);
        return Task.CompletedTask;
    }
}
