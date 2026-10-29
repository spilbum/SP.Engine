using Common.Protocol;
using Common.Protocol.CS2CC;
using SP.Engine.Runtime.Protocol;

namespace ChatClient.Protocol;

[ProtocolHandler(ProtocolId.CS2CC.LoginAck)]
public class LoginAckHandler : ProtocolHandlerBase<UserPeer, LoginAck>
{
    protected override void ExecuteProtocol(UserPeer context, LoginAck protocol)
    {
        context.IsLoggedIn = true;
    }
}
