using Common.Protocol;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Command;

namespace EchoClient.Command;

[CommandHandler(ProtocolId.ES2EC.TcpEchoAck)]
public class TcpEchoAckHandler : CommandHandlerBase<UserPeer, TcpEchoAck>
{
    protected override void ExecuteCommand(UserPeer context, TcpEchoAck protocol)
    {
        
    }
}
