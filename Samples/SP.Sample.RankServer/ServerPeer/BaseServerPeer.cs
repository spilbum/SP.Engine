using SP.Engine.Server;

namespace SP.Sample.RankServer.ServerPeer;

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
