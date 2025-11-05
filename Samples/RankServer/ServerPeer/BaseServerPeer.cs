using SP.Engine.Server;

namespace RankServer.ServerPeer;

public class BaseServerPeer : BasePeer
{
    public BaseServerPeer(ISession session)
        : base(PeerKind.Server, session)
    {
    }

    protected BaseServerPeer(BaseServerPeer other)
        : base(other)
    {
    }
}
