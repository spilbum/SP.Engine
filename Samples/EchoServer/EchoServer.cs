using EchoServer.Connector;
using SP.Engine.Server;
using SP.Engine.Server.Connector;

namespace EchoServer;

public class EchoServer : Engine
{
    protected override bool TryCreatePeer(ISession session, out IPeer peer)
    {
        peer = new EchoPeer(session);
        return true;
    }

    protected override bool TryCreateConnector(string name, out IServerConnector? connector)
    {
        switch (name)
        {
            case "Dummy":
                connector = new DummyConnector();
                return true;
            default:
                connector = null;
                return false;
        }
    }
}
