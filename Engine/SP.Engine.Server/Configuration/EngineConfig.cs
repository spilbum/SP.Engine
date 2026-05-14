using System.Collections.Generic;

namespace SP.Engine.Server.Configuration;

public interface IEngineConfig
{
    NetworkConfig Network { get; }
    SessionConfig Session { get; }
    PerfConfig Perf { get; }
}

public sealed record EngineConfig : IEngineConfig
{
    public List<ListenerConfig> Listeners { get; init; } = [];
    public List<ConnectorConfig> Connectors { get; init; } = [];
    public NetworkConfig Network { get; init; } = new();
    public SessionConfig Session { get; init; } = new();
    public PerfConfig Perf { get; init; } = new();
}
