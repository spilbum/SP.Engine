using System;
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

public class EngineConfigBuilder
{
    private readonly List<ConnectorConfig> _connectors = [];
    private readonly List<ListenerConfig> _listeners = [];
    private NetworkConfig _network = new();
    private PerfConfig _perf = new();
    private SessionConfig _session = new();

    public static EngineConfigBuilder Create()
    {
        return new EngineConfigBuilder();
    }

    public EngineConfigBuilder WithNetwork(Func<NetworkConfig, NetworkConfig> configure)
    {
        _network = configure(_network);
        return this;
    }

    public EngineConfigBuilder WithSession(Func<SessionConfig, SessionConfig> configure)
    {
        _session = configure(_session);
        return this;
    }

    public EngineConfigBuilder WithPerf(Func<PerfConfig, PerfConfig> configure)
    {
        _perf = configure(_perf);
        return this;
    }

    public EngineConfigBuilder AddListener(ListenerConfig listener)
    {
        _listeners.Add(listener);
        return this;
    }

    public EngineConfigBuilder AddConnector(ConnectorConfig connector)
    {
        _connectors.Add(connector);
        return this;
    }

    public EngineConfig Build()
    {
        return new EngineConfig
        {
            Network = _network,
            Session = _session,
            Perf = _perf,
            Listeners = _listeners,
            Connectors = _connectors
        };
    }
}
