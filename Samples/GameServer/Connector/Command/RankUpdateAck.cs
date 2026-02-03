using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Connector.Command;

[ProtocolCommand(R2GProtocol.RankUpdateAck)]
public class RankUpdateAck : BaseCommand<RankConnector, R2GProtocolData.RankUpdateAck>
{
    protected override Task ExecuteCommand(RankConnector context, R2GProtocolData.RankUpdateAck protocol)
    {
        context.Logger.Debug("RankUpdateAck - result={0}, kind={1}, uid={2}", protocol.Result, protocol.SeasonKind,
            protocol.Uid);
        return Task.CompletedTask;
    }
}
