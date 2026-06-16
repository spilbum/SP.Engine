using EchoServer.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.Command;

[ProtocolCommand(S2C.UdpEchoAck)]
public class UdpEchoAck : CommandBase<EchoClient, S2C_UdpEchoAck>
{
    protected override void ExecuteCommand(EchoClient context, S2C_UdpEchoAck protocol)
    {
        
    }
}
