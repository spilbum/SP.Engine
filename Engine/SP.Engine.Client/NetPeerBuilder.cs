using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SP.Core.Logging;
using SP.Engine.Client.Configuration;

namespace SP.Engine.Client
{
    public class NetPeerBuilder
    {
        private readonly List<Action<EngineConfig>> _configActions = new List<Action<EngineConfig>>();
        private readonly HashSet<Assembly> _assemblies = new HashSet<Assembly>();
        private ILogger _logger;
        private bool _isBuilt;
        
        public static NetPeerBuilder Create() => new NetPeerBuilder();

        private NetPeerBuilder Configure(Action<EngineConfig> configureAction)
        {
            if (_isBuilt)
                throw new InvalidOperationException("Cannot modify builder after Build/Initialize has been called.");
            
            if (configureAction != null)
            {
                _configActions.Add(configureAction);
            }
            
            return this;
        }

        public NetPeerBuilder WithAutoPing(bool enable, int intervalSec)
            => Configure(c =>
            {
                c.EnableAutoPing = enable;
                c.AutoPingIntervalSec = intervalSec;
            });

        public NetPeerBuilder WithConnectAttempts(int maxAttempts, int intervalSec)
            => Configure(c =>
            {
                c.MaxConnectAttempts = maxAttempts;
                c.ConnectAttemptIntervalSec = intervalSec;
            });

        public NetPeerBuilder WithReconnectAttempts(int maxAttempts, int intervalSec)
            => Configure(c =>
            {
                c.MaxReconnectAttempts = maxAttempts;
                c.ReconnectAttemptIntervalSec = intervalSec;
            });
        
        public NetPeerBuilder WithUdpMtu(ushort mtu) => Configure(c => { c.UdpMtu = mtu; });

        public NetPeerBuilder WithKeepAlive(bool enable, uint timeSec, uint intervalSec)
            => Configure(c =>
            {
                c.EnableKeepAlive = enable;
                c.KeepAliveTimeSec = timeSec;
                c.KeepAliveIntervalSec = intervalSec;
            });

        public NetPeerBuilder WithUdpHealthCheck(int intervalSec, int threshold)
            => Configure(c =>
            {
                c.UdpHealthCheckIntervalSec = intervalSec;
                c.UdpHealthCheckThreshold = threshold;
            });

        public NetPeerBuilder WithBufferSize(int sendBufferSize, int receiveBufferSize)
            => Configure(c =>
            {
                c.SendBufferSize = sendBufferSize;
                c.ReceiveBufferSize = receiveBufferSize;
            });
        
        public NetPeerBuilder WithLogger(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            return this;
        }

        public NetPeerBuilder AddAssembly(Assembly assembly)
        {
            if (assembly != null)
            {
                _assemblies.Add(assembly);
            }
            
            return this;
        }

        public NetPeerBuilder WithEntryAssembly()
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly == null)
            {
                throw new InvalidOperationException(
                    "Could not resolve EntryAssembly. Specify the assembly explicitly via AddAssembly().");
            }
            
            _assemblies.Add(assembly);
            return this;
        }

        public bool TryInitialize<T>(T instance) where T : NetPeerBase
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (_logger == null) throw new InvalidOperationException("Logger must be configured.");

            if (_assemblies.Count == 0)
            {
                _logger.Warn(
                    "No assemblies configured for command discovery. Did you forget to call WithAssembly() or WithEntryAssembly()?");
            }
            
            var config = new EngineConfig();
            foreach (var action in _configActions)
            {
                action(config);
            }
            
            if (!instance.InternalInitialize(_assemblies.ToArray(), config, _logger))
                return false;
            
            _isBuilt = true;
            return true;
        }
        
        public T Build<T>() where T : NetPeerBase, new()
        {
            var peer = new T();
            if (!TryInitialize(peer))
            {
                throw new InvalidOperationException(
                    $"NetPeer initialization failed for type {typeof(T).Name}. Check logs for details.");
            }
            return peer;
        }
    }
}
