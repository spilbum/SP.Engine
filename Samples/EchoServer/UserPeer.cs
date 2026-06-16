using SP.Engine.Server;

namespace EchoServer;

public class UserPeer(Session session) : PeerBase(PeerKind.User, session)
{
    
}
