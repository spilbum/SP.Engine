using Common.Protocol;
using Common.Protocol.CC2CS;
using SP.Engine.Runtime.Command;

namespace ChatServer.Command;

[CommandHandler(ProtocolId.CC2CS.ChatNotifyReq)]
public class ChatNotifyReqHandler : CommandHandlerBase<UserPeer, ChatNotifyReq>
{
    protected override void ExecuteCommand(UserPeer context, ChatNotifyReq protocol)
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
