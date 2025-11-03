using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;

namespace SP.Sample.GameServer.Connector.Command;

[ProtocolCommand(S2SProtocol.RegisterAck)]
public class RegisterAck : BaseCommand<RankConnector, S2SProtocolData.RegisterAck>
{
    protected override void ExecuteProtocol(RankConnector context, S2SProtocolData.RegisterAck protocol)
    {
        context.OnRegisterAck(protocol.Result);
    }
}
