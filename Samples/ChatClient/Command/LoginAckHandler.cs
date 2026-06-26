using Common.Protocol;
using Common.Protocol.CS2CC;
using SP.Engine.Runtime.Command;

namespace ChatClient.Command;

[CommandHandler(ProtocolId.CS2CC.LoginAck)]
public class LoginAckHandler : CommandHandlerBase<UserPeer, LoginAck>
{
    protected override void ExecuteCommand(UserPeer context, LoginAck protocol)
    {
        context.IsLoggedIn = true;
    }
}
