using Common;
using SP.Engine.Client.Command;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.ProtocolHandler;

[ProtocolCommand(S2CProtocol.TcpEchoAck)]
public class TcpEchoAck : BaseCommand<EchoClient, S2CProtocolData.TcpEchoAck>
{
    protected override void ExecuteProtocol(EchoClient peer, S2CProtocolData.TcpEchoAck protocol)
    {
        peer.Logger.Debug("[TcpEchoAck] Received");
    }
}
