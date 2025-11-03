using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;

namespace SP.Sample.GameClient.Command;

[ProtocolCommand(G2CProtocol.RankTopAck)]
public class RankTopAck : BaseCommand<GameClient, G2CProtocolData.RankTopAck>
{
    protected override void ExecuteProtocol(GameClient context, G2CProtocolData.RankTopAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RankTopAck failed. result={0}, kind={1}", protocol.Result, protocol.SeasonKind);
            return;
        }

        foreach (var info in protocol.Infos!)
            context.Logger.Debug("RankTopAck - kind={0}, rank={1}, uid={2}, score={3}, name={4}, countryCode={5}",
                protocol.SeasonKind, info.Rank, info.Uid, info.Name, info.CountryCode, info.CountryCode);
    }
}
