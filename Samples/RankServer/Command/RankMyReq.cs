using Common;
using RankServer.ServerPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace RankServer.Command;

[ProtocolCommand(G2RProtocol.RankMyReq)]
public class RankMyReq : BaseCommand<GameServerPeer, G2RProtocolData.RankMyReq>
{
    protected override void ExecuteProtocol(GameServerPeer context, G2RProtocolData.RankMyReq protocol)
    {
        var ack = new R2GProtocolData.RankMyAck
            { Result = ErrorCode.Unknown, SeasonKind = protocol.SeasonKind, Uid = protocol.Uid };

        if (!RankServer.Instance.TryGetSeason(protocol.SeasonKind, out var season) ||
            !season!.TryGetInfo(protocol.Uid, out var info))
        {
            ack.Result = ErrorCode.RankNotFound;
            context.Send(ack);
            return;
        }

        ack.Result = ErrorCode.Ok;
        ack.Rank = info!.Rank;
        ack.Score = info.Score;
        ack.Info = info;
        context.Send(ack);
    }
}
