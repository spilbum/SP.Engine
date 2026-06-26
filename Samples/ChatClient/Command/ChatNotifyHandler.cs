using Common.Protocol;
using Common.Protocol.CS2CC;
using SP.Engine.Runtime.Command;

namespace ChatClient.Command;

[CommandHandler(ProtocolId.CS2CC.ChatNotify)]
public class ChatNotifyHandler : CommandHandlerBase<UserPeer, ChatNotify>
{
    protected override void ExecuteCommand(UserPeer context, ChatNotify protocol)
    {
        context.Logger.Debug("[{0}] Received from {1}: {2}", context.UserId, protocol.FromUserId, protocol.Message);
    }
}
