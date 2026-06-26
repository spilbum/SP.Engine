using Common.Protocol;
using Common.Protocol.EC2ES;
using Common.Protocol.ES2EC;
using SP.Engine.Runtime.Command;
using SP.Engine.Server.Protocol;

namespace EchoServer.Command;

[CommandHandler(ProtocolId.EC2ES.TcpEchoReq)]
public class TcpEchoReqHandler : CommandHandlerBase<UserPeer, TcpEchoReq>
{
    protected override void ExecuteCommand(UserPeer context, TcpEchoReq protocol)
    {
        context.Send(new TcpEchoAck { SentTicks = protocol.SentTicks, Data = protocol.Data });
    }
}
