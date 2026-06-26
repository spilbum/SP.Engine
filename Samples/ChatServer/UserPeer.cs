using Common.Protocol.CS2CC;
using SP.Engine.Runtime;
using SP.Engine.Server;
using SP.Engine.Server.Protocol;

namespace ChatServer;

public class UserPeer(Session session) : PeerBase(PeerKind.User, session)
{
    public string? UserId { get; private set; }

    public void SetUserId(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentNullException(nameof(userId));
        
        UserId = userId;
        ChatServer.Instance.Bind(this);
    }

    public void SendChat(string? fromUserId, string? message)
    {
        Send(new ChatNotify { FromUserId = fromUserId, Message = message });
    }

    protected override void OnOnline()
    {
        base.OnOnline();

        ChatServer.Instance.Bind(this);
    }

    protected override void OnOffline(CloseReason reason)
    {
        base.OnOffline(reason);
        
        ChatServer.Instance.Unbind(this);
    }
}
