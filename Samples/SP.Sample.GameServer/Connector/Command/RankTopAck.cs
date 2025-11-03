using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;

namespace SP.Sample.GameServer.Connector.Command;

[ProtocolCommand(R2GProtocol.RankTopAck)]
public class RankTopAck : BaseCommand<RankConnector, R2GProtocolData.RankTopAck>
{
    protected override void ExecuteProtocol(RankConnector context, R2GProtocolData.RankTopAck protocol)
    {
        if (!GameServer.Instance.TryGetPeer(protocol.Uid, out var peer))
            return;

        var ack = new G2CProtocolData.RankTopAck
        {
            Result = protocol.Result,
            SeasonKind = protocol.SeasonKind,
            Infos = protocol.Infos
        };
        if (!peer!.Send(ack))
            context.Logger.Warn("Failed to send RankMyAck");
    }
}
