using System;
using System.Collections.Generic;

namespace SP.Engine.Server.Configuration
{
    public interface IEngineConfig
    {
        NetworkConfig Network { get; }
        SessionConfig Session { get; }
        RuntimeConfig Runtime { get; }
    }
    
    public sealed record EngineConfig : IEngineConfig
    {
        public NetworkConfig Network { get; init; } = new();
        public SessionConfig Session { get; init; } = new();
        public RuntimeConfig Runtime { get; init; } = new();
        public List<ListenerConfig> Listeners { get; init; } = [];
        public List<ConnectorConfig> Connectors { get; init; } = [];
    }

    public class EngineConfigBuilder
    {
        private NetworkConfig _network = new();
        private SessionConfig _session = new();
        private RuntimeConfig _runtime = new();
        private readonly List<ListenerConfig> _listeners = [];
        private readonly List<ConnectorConfig> _connectors = [];

        public static EngineConfigBuilder Create() => new();

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
        
        public EngineConfigBuilder WithRuntime(Func<RuntimeConfig, RuntimeConfig> configure)
        {
            _runtime = configure(_runtime);
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
                Runtime = _runtime,
                Listeners = _listeners,
                Connectors = _connectors
            };
        }
    }
}
