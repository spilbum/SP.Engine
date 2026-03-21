using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Connector;

public interface IConnector
{
    string Name { get; }
    string Host { get; }
    int Port { get; }
    void Close();
    bool Send(IProtocolData data);
}

