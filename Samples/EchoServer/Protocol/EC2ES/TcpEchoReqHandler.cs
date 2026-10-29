using Common.Protocol;
using Common.Protocol.EC2ES;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Protocol;

namespace EchoServer.Protocol;

[ProtocolHandler(ProtocolId.EC2ES.TcpEchoReq)]
public class TcpEchoReqHandler : ProtocolHandlerBase<UserPeer, TcpEchoReq>
{
    protected override void ExecuteProtocol(UserPeer context, TcpEchoReq protocol)
    {
        context.Send(new TcpEchoAck { SentTicks = protocol.SentTicks, Data = protocol.Data });
    }
}
