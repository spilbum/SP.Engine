using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;

namespace SP.Sample.GameClient.Command;

[ProtocolCommand(G2CProtocol.RankMyAck)]
public class RankMyAck : BaseCommand<GameClient, G2CProtocolData.RankMyAck>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.RankMyAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RankMyAck failed. result={0}, kind={1}", protocol.Result, protocol.SeasonKind);
            return;
        }

        var info = protocol.Info;
        context.Logger.Debug("RankMyAck - kind={0}, rank={1}, score={2}, name={3}, countryCode={4}",
            protocol.SeasonKind, protocol.Rank, protocol.Score, info?.Name, info?.CountryCode);
    }
}
