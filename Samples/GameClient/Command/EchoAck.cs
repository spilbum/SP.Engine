using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.EchoAck)]
public class EchoAck : BaseCommand<Client, G2CProtocolData.EchoAck>
{
    protected override void ExecuteCommand(Client context, G2CProtocolData.EchoAck protocol)
    {
        context.OnEchoAck(protocol);
    }
}
