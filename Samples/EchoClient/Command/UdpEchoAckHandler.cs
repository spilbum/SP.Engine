using Common.Protocol;
using Common.Protocol.S2C;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.Command;

[ProtocolCommand(ProtocolId.S2C.UdpEchoAck)]
public class UdpEchoAckHandler : CommandHandlerBase<EchoClient, UdpEchoAck>
{
    protected override void ExecuteCommand(EchoClient context, UdpEchoAck protocol)
    {
        
    }
}
