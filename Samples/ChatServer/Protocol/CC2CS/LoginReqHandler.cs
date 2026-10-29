using Common.Protocol;
using Common.Protocol.CC2CS;
using SP.Engine.Runtime.Protocol;

namespace ChatServer.Protocol;

[ProtocolHandler(ProtocolId.CC2CS.LoginReq)]
public class LoginReqHandler : ProtocolHandlerBase<UserPeer, LoginReq>
{
    protected override void ExecuteProtocol(UserPeer context, LoginReq protocol)
    {
        context.SetUserId(protocol.UserId);
    }
}
