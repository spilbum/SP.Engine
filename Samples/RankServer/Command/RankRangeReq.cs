using Common;
using RankServer.ServerPeer;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace RankServer.Command;

[ProtocolCommand(G2RProtocol.RankRangeReq)]
public class RankRangeReq : BaseCommand<GameServerPeer, G2RProtocolData.RankRangeReq>
{
    protected override Task ExecuteCommand(GameServerPeer context, G2RProtocolData.RankRangeReq protocol)
    {
        var ack = new R2GProtocolData.RankRangeAck
            { Result = ErrorCode.Unknown, SeasonKind = protocol.SeasonKind, Uid = protocol.Uid };

        try
        {
            if (!RankServer.Instance.TryGetSeason(protocol.SeasonKind, out var season) ||
                !season!.TryGetRangeInfos(protocol.StartRank, protocol.Count, out var infos))
            {
                ack.Result = ErrorCode.RankNotFound;
                return Task.CompletedTask;
            }

            ack.Result = ErrorCode.Ok;
            ack.Infos = infos;
        }
        catch (Exception e)
        {
            context.Logger.Error(e);
            ack.Result = ErrorCode.InternalError;
        }
        finally
        {
            if (!context.Send(ack))
                context.Logger.Warn("Failed to send RankRangeAck");
        }
        
        return Task.CompletedTask;
    }
}
