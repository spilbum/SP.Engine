using Common.Protocol;
using Common.Protocol.EC2ES;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Protocol;

namespace EchoServer.Protocol;

[ProtocolHandler(ProtocolId.EC2ES.UdpEchoReq)]
public class UdpEchoReqHandler : ProtocolHandlerBase<UserPeer, UdpEchoReq>
{
    protected override void ExecuteProtocol(UserPeer context, UdpEchoReq protocol)
    {
        context.Send(new UdpEchoAck { SentTicks = protocol.SentTicks, Data = protocol.Data });
    }
}
