using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Connector.Command;

[ProtocolCommand(R2GProtocol.RankRangeAck)]
public class RankRangeAck : BaseCommand<RankConnector, R2GProtocolData.RankRangeAck>
{
    protected override Task ExecuteCommand(RankConnector context, R2GProtocolData.RankRangeAck protocol)
    {
        if (!GameServer.Instance.TryGetPeer(protocol.Uid, out var peer))
            return Task.CompletedTask;

        var ack = new G2CProtocolData.RankRangeAck
        {
            Result = protocol.Result,
            SeasonKind = protocol.SeasonKind,
            Infos = protocol.Infos
        };

        if (!peer!.Send(ack))
            context.Logger.Warn("Failed to send RankRangeAck");
        return Task.CompletedTask;
    }
}
