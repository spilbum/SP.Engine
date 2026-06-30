using Common.Protocol;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Command;

namespace EchoClient.Command;

[CommandHandler(ProtocolId.ES2EC.UdpEchoAck)]
public class UdpEchoAckHandler : CommandHandlerBase<UserPeer, UdpEchoAck>
{
    protected override void ExecuteCommand(UserPeer context, UdpEchoAck protocol)
    {
        
    }
}
