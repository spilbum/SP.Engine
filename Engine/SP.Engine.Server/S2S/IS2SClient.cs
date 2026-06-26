using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server.S2S;

public interface IS2SClient
{
    string Name { get; }
    string Host { get; }
    int Port { get; }
    void Close();
    bool Send(IProtocolData data);
}

