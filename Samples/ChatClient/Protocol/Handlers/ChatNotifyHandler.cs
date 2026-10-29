using Common.Protocol;
using Common.Protocol.CS2CC;
using SP.Engine.Runtime.Protocol;

namespace ChatClient.Protocol;

[ProtocolHandler(ProtocolId.CS2CC.ChatNotify)]
public class ChatNotifyHandler : ProtocolHandlerBase<UserPeer, ChatNotify>
{
    protected override void ExecuteProtocol(UserPeer context, ChatNotify protocol)
    {
        context.Logger.Debug("[{0}] Received from {1}: {2}", context.UserId, protocol.FromUserId, protocol.Message);
    }
}
