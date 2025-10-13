using Common;
using SP.Engine.Client.Command;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.Command;

[ProtocolCommand(S2CProtocol.UdpEchoAck)]
public class UdpEchoAck : BaseCommand<EchoClient, S2CProtocolData.UdpEchoAck>
{
    protected override void ExecuteProtocol(EchoClient peer, S2CProtocolData.UdpEchoAck protocol)
    {
        peer.Logger.Debug("[UdpEchoAck] Received");
    }
}
