using EchoServer.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.Command;

[ProtocolCommand(S2C.TcpEchoAck)]
public class TcpEchoAck : CommandBase<EchoClient, S2C_TcpEchoAck>
{
    protected override void ExecuteCommand(EchoClient context, S2C_TcpEchoAck protocol)
    {
        
    }
}
