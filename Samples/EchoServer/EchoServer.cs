using SP.Engine.Server;
using SP.Engine.Server.Connector;

namespace EchoServer;

public class EchoServer : Engine<EchoPeer>
{
    protected override bool TryCreatePeer(ISession session, out EchoPeer peer)
    {
        peer = new EchoPeer(session);
        return true;
    }

    protected override bool TryCreateConnector(string name, out IServerConnector connector)
    {
        connector = new DummyConnector(name);
        return true;
    }
}
