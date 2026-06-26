using Common.Protocol;
using Common.Protocol.EC2ES;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Command;
using SP.Engine.Server.Protocol;

namespace EchoServer.Command;

[CommandHandler(ProtocolId.EC2ES.UdpEchoReq)]
public class UdpEchoReqHandler : CommandHandlerBase<UserPeer, UdpEchoReq>
{
    protected override void ExecuteCommand(UserPeer context, UdpEchoReq protocol)
    {
        context.Send(new UdpEchoAck { SentTicks = protocol.SentTicks, Data = protocol.Data });
    }
}
