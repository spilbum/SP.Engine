using Common.Protocol;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Protocol;

namespace EchoClient.Protocol.ES2EC;

[ProtocolHandler(ProtocolId.ES2EC.UdpEchoAck)]
public class UdpEchoAckHandler : ProtocolHandlerBase<UserPeer, UdpEchoAck>
{
    protected override void ExecuteProtocol(UserPeer context, UdpEchoAck protocol)
    {
        
    }
}
