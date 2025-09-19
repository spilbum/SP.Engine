using SP.Engine.Server.Connector;

namespace TestServer;

public class DummyConnector(string name) : BaseConnector(name)
{
}
