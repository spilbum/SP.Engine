using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.RankRangeAck)]
public class RankRangeAck : BaseCommand<NetworkClient, G2CProtocolData.RankRangeAck>
{
    protected override void ExecuteProtocol(NetworkClient context, G2CProtocolData.RankRangeAck protocol)
    {
        if (protocol.Result != ErrorCode.Ok)
        {
            context.Logger.Error("RankRangeAck failed. result={0}, kind={1}", protocol.Result, protocol.SeasonKind);
            return;
        }

        foreach (var info in protocol.Infos!)
            context.Logger.Debug("RankRangeAck - kind={0}, rank={1}, uid={2}, score={3}, name={4}, countryCode={5}",
                protocol.SeasonKind, info.Rank, info.Uid, info.Name, info.CountryCode, info.CountryCode);
    }
}
