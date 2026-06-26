using SP.Engine.Server;
using SP.Engine.Server.S2S;

namespace ChatServer;

public sealed class ChatServerPeer(Session session) : S2SPeerBase(session)
{
    
}
