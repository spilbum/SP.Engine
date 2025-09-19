using SP.Engine.Runtime;
using SP.Engine.Server;

namespace TestServer;

public class GamePeer(IClientSession session) : BasePeer(EPeerType.User, session)
{
    protected override void OnJoinServer()
    {
        Logger?.Info("Peer joined. {0}", this);
    }

    protected override void OnOnline()
    {
        Logger?.Info("Peer online. {0}", this);
    }

    protected override void OnOffline(ECloseReason reason)
    {
        Logger?.Info("Peer offline. reason={0}, {1}", reason, this);
    }

    protected override void OnLeaveServer(ECloseReason reason)
    {
        Logger?.Info("Peer left. reason={0}, {1}", reason, this);
    }
}
