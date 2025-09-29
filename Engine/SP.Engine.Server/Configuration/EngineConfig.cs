using System;
using System.Collections.Generic;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server.Configuration
{
    public sealed record EngineConfig
    {
        public NetworkConfig Network { get; init; } = new();
        public SessionConfig Session { get; init; } = new();
        public SecurityConfig Security { get; init; } = new();
        public RuntimeConfig Runtime { get; init; } = new();
        public IReadOnlyList<ListenerConfig> Listeners { get; init; } = Array.Empty<ListenerConfig>();
        public IReadOnlyList<ConnectorConfig> Connectors { get; init; } = Array.Empty<ConnectorConfig>();
    }

    public class EngineConfigBuilder
    {
        private NetworkConfig _network = new();
        private SessionConfig _session = new();
        private SecurityConfig _security = new();
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

        public EngineConfigBuilder WithSecurity(Func<SecurityConfig, SecurityConfig> configure)
        {
            _security = configure(_security);
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
                Security = _security,
                Runtime = _runtime,
                Listeners = _listeners.ToArray(),
                Connectors = _connectors.ToArray(),
            };
        }
    }
}
