using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Connector.Command;

[ProtocolCommand(R2GProtocol.RankMyAck)]
public class RankMyAck : BaseCommand<RankConnector, R2GProtocolData.RankMyAck>
{
    protected override Task ExecuteCommand(RankConnector context, R2GProtocolData.RankMyAck protocol)
    {
        if (!GameServer.Instance.TryGetPeer(protocol.Uid, out var peer))
            return Task.CompletedTask;

        var ack = new G2CProtocolData.RankMyAck
        {
            Result = protocol.Result,
            SeasonKind = protocol.SeasonKind,
            Rank = protocol.Rank,
            Score = protocol.Score,
            Info = protocol.Info
        };
        if (!peer!.Send(ack))
            context.Logger.Warn("Failed to send RankMyAck");
        return Task.CompletedTask;
    }
}
