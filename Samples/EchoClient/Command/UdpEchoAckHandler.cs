using Common.Protocol;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Command;

namespace EchoClient.Command;

[CommandHandler(ProtocolId.ES2EC.UdpEchoAck)]
public class UdpEchoAckHandler : CommandHandlerBase<EchoClient, UdpEchoAck>
{
    protected override void ExecuteCommand(EchoClient context, UdpEchoAck protocol)
    {
        
    }
}
