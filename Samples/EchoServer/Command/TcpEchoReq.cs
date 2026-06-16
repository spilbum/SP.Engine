using EchoServer.Protocol;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Protocol;

namespace EchoServer.Command;

[ProtocolCommand(C2S.TcpEchoReq)]
public class TcpEchoReq : CommandBase<UserPeer, C2S_TcpEchoReq>
{
    protected override void ExecuteCommand(UserPeer context, C2S_TcpEchoReq protocol)
    {
        using var scope = ProtocolScope<S2C_TcpEchoAck>.Rent();
        scope.Protocol.SentTicks = protocol.SentTicks;
        scope.Protocol.Data = protocol.Data;
        context.Send(scope.Protocol);
    }
}
