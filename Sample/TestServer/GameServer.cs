using SP.Engine.Server;
using SP.Engine.Server.Connector;

namespace TestServer;

public class GameServer : Engine<GamePeer>
{
    protected override GamePeer CreatePeer(IClientSession<GamePeer> session)
    {
        return new GamePeer(session);
    }

    protected override IServerConnector CreateConnector(string name)
    {
        return new DummyConnector(name);
    }
}
