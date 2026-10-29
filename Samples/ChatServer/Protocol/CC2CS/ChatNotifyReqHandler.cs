using Common.Protocol;
using Common.Protocol.CC2CS;
using SP.Engine.Runtime.Protocol;

namespace ChatServer.Protocol;

[ProtocolHandler(ProtocolId.CC2CS.ChatNotifyReq)]
public class ChatNotifyReqHandler : ProtocolHandlerBase<UserPeer, ChatNotifyReq>
{
    protected override void ExecuteProtocol(UserPeer context, ChatNotifyReq protocol)
    {
        var targetUser = ChatServer.Instance.GetUserPeer(protocol.TargetUserId);
        if (targetUser == null)
        {
            ChatServer.Instance.BroadcastChat(context.UserId, protocol.TargetUserId, protocol.Message);
            return;
        }

        targetUser.SendChat(context.UserId, protocol.Message);
    }
}
