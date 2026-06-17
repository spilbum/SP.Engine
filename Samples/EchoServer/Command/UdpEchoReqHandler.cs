using Common.Protocol;
using Common.Protocol.C2S;
using Common.Protocol.S2C;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Protocol;

namespace EchoServer.Command;

[ProtocolCommand(ProtocolId.C2S.UdpEchoReq)]
public class UdpEchoReqHandler : CommandHandlerBase<UserPeer, UdpEchoReq>
{
    protected override void ExecuteCommand(UserPeer context, UdpEchoReq protocol)
    {
        using var scope = ProtocolScope<UdpEchoAck>.Rent();
        scope.Protocol.SentTicks = protocol.SentTicks;
        scope.Protocol.Data = protocol.Data;
        context.Send(scope.Protocol);
    }
}
