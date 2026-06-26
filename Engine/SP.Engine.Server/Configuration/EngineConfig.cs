using System.Collections.Generic;

namespace SP.Engine.Server.Configuration;

public interface IEngineConfig
{
    NetworkConfig Network { get; }
    SessionConfig Session { get; }
    PerfConfig Perf { get; }
}

public sealed class EngineConfig : IEngineConfig
{
    public List<ListenerConfig> Listeners { get; init; } = [];
    public List<S2SClientConfig> S2SClients { get; init; } = [];
    public NetworkConfig Network { get; } = new();
    public SessionConfig Session { get; } = new();
    public PerfConfig Perf { get; } = new();
}
