using SP.Engine.Server.Connector;

namespace EchoServer;

public class DummyConnector(string name) : BaseConnector(name)
{
}
