using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SP.Core.Logging;

namespace SP.Engine.Client.Configuration
{
    public class NetPeerBuilder
    {
        private readonly EngineConfig _config = new EngineConfig();
        private readonly List<Assembly> _assemblies = new List<Assembly>();
        private ILogger _logger;
        
        public static NetPeerBuilder Create() => new NetPeerBuilder();

        public NetPeerBuilder WithAutoPing(bool enable, int intervalSec)
        {
            _config.EnableAutoPing = enable;
            _config.AutoPingIntervalSec = intervalSec;
            return this;
        }

        public NetPeerBuilder WithConnectionAttempts(int maxAttempts, int intervalSec)
        {
            _config.MaxConnectAttempts = maxAttempts;
            _config.ConnectAttemptIntervalSec = intervalSec;
            return this;
        }

        public NetPeerBuilder WithReconnectAttempts(int maxAttempts, int intervalSec)
        {
            _config.MaxReconnectAttempts = maxAttempts;
            _config.ReconnectAttemptIntervalSec = intervalSec;
            return this;
        }

        public NetPeerBuilder WithUdpMtu(ushort mtu)
        {
            _config.UdpMtu = mtu;
            return this;
        }

        public NetPeerBuilder WithKeepAlive(bool enable, int timeSec, int intervalSec)
        {
            _config.EnableKeepAlive = enable;
            _config.KeepAliveTimeSec = timeSec;
            _config.KeepAliveIntervalSec = intervalSec;
            return this;
        }

        public NetPeerBuilder WithUdpHealthCheck(int intervalSec, int threshold)
        {
            _config.UdpHealthCheckIntervalSec = intervalSec;
            _config.UdpHealthCheckThreshold = threshold;
            return this;
        }

        public NetPeerBuilder WithLogger(ILogger logger)
        {
            _logger = logger;
            return this;
        }

        public NetPeerBuilder WithAssembly(Assembly assembly)
        {
            if (!_assemblies.Contains(assembly))
                _assemblies.Add(assembly);
            return this;
        }

        public bool Apply<T>(T instance) where T : NetPeerBase
        {
            if (_logger == null) throw new InvalidOperationException("Logger must be configured.");

            var assembly = Assembly.GetEntryAssembly();
            if (assembly != null && !_assemblies.Contains(assembly))
                _assemblies.Add(assembly);
            
            return instance.InternalInitialize(_assemblies.ToArray(), _config, _logger);
        }
        
        public T Build<T>() where T : NetPeerBase, new()
        {
            var peer = new T();
            if (!Apply(peer))
            {
                throw new InvalidOperationException("NetPeer initialization failed. Check logs for details.");
            }
            return peer;
        }
    }
}
