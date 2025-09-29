using SP.Engine.Server;
using SP.Engine.Server.Connector;

namespace EchoServer;

public class EchoServer : Engine<EchoPeer>
{
    protected override EchoPeer CreatePeer(IClientSession<EchoPeer> peer)
    {
        return new EchoPeer(peer);
    }

    protected override IServerConnector CreateConnector(string name)
    {
        return new DummyConnector(name);
    }
}
