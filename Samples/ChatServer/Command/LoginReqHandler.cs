using Common.Protocol;
using Common.Protocol.CC2CS;
using SP.Engine.Runtime.Command;

namespace ChatServer.Command;

[CommandHandler(ProtocolId.CC2CS.LoginReq)]
public class LoginReqHandler : CommandHandlerBase<UserPeer, LoginReq>
{
    protected override void ExecuteCommand(UserPeer context, LoginReq protocol)
    {
        context.SetUserId(protocol.UserId);
    }
}
