using Common;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace GameClient.Command;

[ProtocolCommand(G2CProtocol.UdpEchoAck)]
public class UdpEchoAck : BaseCommand<Client, G2CProtocolData.UdpEchoAck>
{
    protected override void ExecuteCommand(Client context, G2CProtocolData.UdpEchoAck protocol)
    {
        context.OnEchoAck(protocol.Seq, protocol.SentTicks);
    }
}
