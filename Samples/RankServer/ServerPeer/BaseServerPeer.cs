using SP.Engine.Server;

namespace RankServer.ServerPeer;

public class BaseServerPeer : PeerBase
{
    public BaseServerPeer(Session session)
        : base(PeerKind.Server, session)
    {
    }

    protected BaseServerPeer(BaseServerPeer other)
        : base(other)
    {
    }
}
