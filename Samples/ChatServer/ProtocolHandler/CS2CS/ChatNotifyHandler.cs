using Common.Protocol;
using Common.Protocol.CS2CS;
using SP.Core.Logging;
using SP.Engine.Runtime.Protocol;

namespace ChatServer.ProtocolHandler.CS2CS;

[ProtocolHandler(ProtocolId.CS2CS.ChatNotify)]
public class ChatNotifyHandler : ProtocolHandlerBase<ChatServerPeer, ChatNotify>
{
    protected override void ExecuteProtocol(ChatServerPeer context, ChatNotify protocol)
    {
        var targetUser = ChatServer.Instance.GetUserPeer(protocol.TargetUserId);
        targetUser?.SendChat(protocol.FromUserId, protocol.Message);
        
        Logger.Debug("Chat notify sent. T: {0} ({1}), F: {2}, M: {3}",
            protocol.TargetUserId, targetUser != null, protocol.FromUserId, protocol.Message);
    }
}
