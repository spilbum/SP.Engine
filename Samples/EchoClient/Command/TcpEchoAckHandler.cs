using Common.Protocol;
using Common.Protocol.S2C;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.Command;

[ProtocolCommand(ProtocolId.S2C.TcpEchoAck)]
public class TcpEchoAckHandler : CommandHandlerBase<EchoClient, TcpEchoAck>
{
    protected override void ExecuteCommand(EchoClient context, TcpEchoAck protocol)
    {
        
    }
}
