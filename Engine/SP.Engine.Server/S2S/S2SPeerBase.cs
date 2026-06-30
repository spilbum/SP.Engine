using SP.Engine.Runtime;

namespace SP.Engine.Server.S2S;

public abstract class S2SPeerBase(Session session) : PeerBase(PeerKind.Server, session)
{
}


