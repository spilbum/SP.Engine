using SP.Engine.Runtime;
using SP.Engine.Server;

namespace EchoServer;

public class EchoPeer(IClientSession session) : BasePeer(SP.Engine.Server.PeerKind.User, session)
{
    protected override void OnJoinServer()
    {
        Logger?.Info("Peer joined. {0}", this);
    }

    protected override void OnOnline()
    {
        Logger?.Info("Peer online. {0}", this);
    }

    protected override void OnOffline(CloseReason reason)
    {
        Logger?.Info("Peer offline. reason={0}, {1}", reason, this);
    }

    protected override void OnLeaveServer(CloseReason reason)
    {
        Logger?.Info("Peer left. reason={0}, {1}", reason, this);
    }
}
