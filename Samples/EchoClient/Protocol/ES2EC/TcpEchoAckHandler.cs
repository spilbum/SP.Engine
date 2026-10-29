using Common.Protocol;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.Protocol.ES2EC;

[ProtocolHandler(ProtocolId.ES2EC.TcpEchoAck)]
public class TcpEchoAckHandler : ProtocolHandlerBase<UserPeer, TcpEchoAck>
{
    protected override void ExecuteProtocol(UserPeer context, TcpEchoAck protocol)
    {
        
    }
}
