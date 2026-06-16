using EchoServer.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Protocol;

namespace EchoServer.Command;

[ProtocolCommand(C2S.UdpEchoReq)]
public class UdpEchoReq : CommandBase<UserPeer, C2S_UdpEchoReq>
{
    protected override void ExecuteCommand(UserPeer context, C2S_UdpEchoReq protocol)
    {
        using var scope = ProtocolScope<S2C_UdpEchoAck>.Rent();
        scope.Protocol.SentTicks = protocol.SentTicks;
        scope.Protocol.Data = protocol.Data;
        context.Send(scope.Protocol);
    }
}
