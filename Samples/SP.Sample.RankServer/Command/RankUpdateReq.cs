using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.RankServer.ServerPeer;

namespace SP.Sample.RankServer.Command;

[ProtocolCommand(G2RProtocol.RankUpdateReq)]
public class RankUpdateReq : BaseCommand<GameServerPeer, G2RProtocolData.RankUpdateReq>
{
    protected override void ExecuteProtocol(GameServerPeer context, G2RProtocolData.RankUpdateReq protocol)
    {
        var ack = new R2GProtocolData.RankUpdateAck
            { Result = ErrorCode.Unknown, SeasonKind = protocol.SeasonKind, Uid = protocol.Uid };

        try
        {
            if (!RankServer.Instance.TryGetSeason(protocol.SeasonKind, out var season))
            {
                ack.Result = ErrorCode.RankNotFound;
                return;
            }

            if (!season!.UpdateRecord(
                    protocol.Uid,
                    protocol.DeltaScore,
                    protocol.AbsoluteScore,
                    protocol.Profile?.Name,
                    protocol.Profile?.CountryCode))
            {
                ack.Result = ErrorCode.InternalError;
                return;
            }

            ack.Result = ErrorCode.Ok;
        }
        catch (Exception e)
        {
            ack.Result = ErrorCode.InternalError;
            context.Logger.Error(e);
        }
        finally
        {
            context.Send(ack);
        }
    }
}
