using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameServer.Connector.Command;

[ProtocolCommand(S2SProtocol.RegisterAck)]
public class RegisterAck : BaseCommand<RankConnector, S2SProtocolData.RegisterAck>
{
    protected override Task ExecuteCommand(RankConnector context, S2SProtocolData.RegisterAck protocol)
    {
        context.OnRegisterAck(protocol.Result);
        return Task.CompletedTask;
    }
}
