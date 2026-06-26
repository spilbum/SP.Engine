using Common.Protocol;
using Common.Protocol.CS2CS;
using SP.Engine.Runtime.Command;

namespace ChatServer.S2S;

[CommandHandler(ProtocolId.CS2CS.ChatNotify)]
public class ChatNotifyHandler : CommandHandlerBase<ChatServerPeer, ChatNotify>
{
    protected override void ExecuteCommand(ChatServerPeer context, ChatNotify protocol)
    {
        var targetUser = ChatServer.Instance.GetUserPeer(protocol.TargetUserId);
        targetUser?.SendChat(protocol.FromUserId, protocol.Message);
        
        context.Logger.Debug("Chat notify sent. T: {0} ({1}), F: {2}, M: {3}",
            protocol.TargetUserId, targetUser != null, protocol.FromUserId, protocol.Message);
    }
}
