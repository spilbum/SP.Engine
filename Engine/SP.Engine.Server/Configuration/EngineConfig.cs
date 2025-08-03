using System.Collections.Generic;
using System.Threading;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server.Configuration
{
    public interface IEngineConfig
    {
        int SendTimeOutMs { get; }
        int LimitConnectionCount { get; }
        int ReceiveBufferSize { get; }
        int SendBufferSize { get; }
        bool IsDisableClearIdleSession { get; }
        int ClearIdleSessionIntervalSec { get; }
        int IdleSessionTimeOutSec { get; }
        int MaxAllowedLength { get; }
        bool IsDisableSessionSnapshot { get; }
        int SessionsSnapshotIntervalSec { get; }
        bool IsDisableKeepAlive { get; }
        int KeepAliveTimeSec { get; }
        int KeepAliveIntervalSec { get; }
        int SendingQueueSize { get; }
        bool IsLogAllSocketError { get; }
        List<ListenerConfig> Listeners { get; }
        int WaitingReconnectPeerTimeOutSec { get; }
        int WaitingReconnectPeerTimerIntervalSec { get; }
        int HandshakePendingQueueTimerIntervalSec { get; }
        int AuthHandshakeTimeOutSec { get; }
        int CloseHandshakeTimeOutSec { get; }
        int MaxReSendCnt { get; }
        List<ConnectorConfig> Connectors { get; } 
        bool UseEncryption { get; }
        bool UseCompression { get; }
        byte CompressionThresholdPercent { get; }
        int PeerUpdateIntervalMs { get; }
        int ConnectorUpdateIntervalMs { get; }

        int MaxWorkingThreads { get; }
        int MinWorkingThreads { get; }
        int MaxCompletionPortThreads { get; }
        int MinCompletionPortThreads { get; }
        
        PackOptions ToPackOptions();
    }
    public class EngineConfig : IEngineConfig
    {
        private const int DefaultReceiveBufferSize = 4 * 1024;
        private const int DefaultLimitConnectionCount = 100;
        private const int DefaultSendingQueueSize = 5;
        private const int DefaultLimitRequestLength = 4096;
        private const int DefaultSendTimeOutMs = 5000;
        private const int DefaultClearIdleSessionIntervalSec = 120;
        private const int DefaultIdleSessionTimeOutSec = 300;
        private const int DefaultSendBufferSize = 4096;
        private const int DefaultSessionSnapshotIntervalSec = 1;
        private const int DefaultKeepAliveTimeSec = 10;
        private const int DefaultKeepAliveIntervalSec = 2;
        private const int DefaultWaitingReconnectPeerTimeOutSec = 120;
        private const int DefaultWaitingReconnectPeerTimerIntervalSec = 60;
        private const int DefaultHandshakePendingQueueTimerIntervalSec = 60;
        private const int DefaultAuthHandshakeTimeOutSec = 120;
        private const int DefaultCloseHandshakeTimeOutSec = 120;
        private const int DefaultMaxReSendCnt = 5;
        private const bool DefaultUseEncryption = false;
        private const bool DefaultUseCompression = false;
        private const int DefaultCompressionThresholdPercent = 10;
        private const int DefaultPeerUpdateIntervalMs = 30;
        private const int DefaultConnectorUpdateIntervalMs = 30;

        public int SendTimeOutMs { get; set; } = 5000;
        public int LimitConnectionCount { get; set; } = 3000;
        public int ReceiveBufferSize { get; set; } = 64 * 1024;
        public int SendBufferSize { get; set; } = 4 * 1024;
        public bool IsDisableClearIdleSession { get; set; }
        public int ClearIdleSessionIntervalSec { get; set; } = 120;
        public int IdleSessionTimeOutSec { get; set; } = 300;
        public int MaxAllowedLength { get; set; } = 64 * 1024;
        public bool IsDisableSessionSnapshot { get; set; }
        public int SessionsSnapshotIntervalSec { get; set; } = 1;
        public bool IsDisableKeepAlive { get; set; }
        public int KeepAliveTimeSec { get; set; } = 30;
        public int KeepAliveIntervalSec { get; set; } = 2;
        public int SendingQueueSize { get; set; } = 5;
        public bool IsLogAllSocketError { get; set; }
        public List<ListenerConfig> Listeners { get; } = [];
        public int WaitingReconnectPeerTimeOutSec { get; set; } = 120;
        public int WaitingReconnectPeerTimerIntervalSec { get; set; } = 60;
        public int HandshakePendingQueueTimerIntervalSec { get; set; } = 60;
        public int AuthHandshakeTimeOutSec { get; set; } = 120;
        public int CloseHandshakeTimeOutSec { get; set; } = 120;
        public int MaxReSendCnt { get; set; } = 5;
        public List<ConnectorConfig> Connectors { get; set; } = [];
        public bool UseEncryption { get; set; } = true;
        public bool UseCompression { get; set; } = true;
        public byte CompressionThresholdPercent { get; set; } = 20;
        public int PeerUpdateIntervalMs { get; set; } = 50;
        public int ConnectorUpdateIntervalMs { get; set; } = 30;
        
        public int MaxWorkingThreads { get; set; }
        public int MinWorkingThreads { get; set; }
        public int MaxCompletionPortThreads { get; set; }
        public int MinCompletionPortThreads { get; set; }
        
        public EngineConfig()
        {
            ThreadPool.GetMaxThreads(out var maxWorkingThreads, out var maxCompletionPortThreads);
            MaxWorkingThreads = maxWorkingThreads;
            MaxCompletionPortThreads = maxCompletionPortThreads;
            ThreadPool.GetMinThreads(out var minWorkingThreads, out var minCompletionPortThreads);
            MinWorkingThreads = minWorkingThreads;
            MinCompletionPortThreads = minCompletionPortThreads;
        }

        public PackOptions ToPackOptions() => new()
        {
            UseEncryption = UseEncryption,
            UseCompression = UseCompression,
            CompressionThresholdPercent = CompressionThresholdPercent
        };
    }

    public class EngineConfigBuilder
    {
        private readonly EngineConfig _config = new();

        public static EngineConfigBuilder Create() => new();

        public EngineConfigBuilder WithSendTimeout(int ms)
        {
            _config.SendTimeOutMs = ms;
            return this;
        }

        public EngineConfigBuilder WithLimitConnectionCount(int count)
        {
            _config.LimitConnectionCount = count;
            return this;
        }

        public EngineConfigBuilder WithReceiveBufferSize(int size)
        {
            _config.ReceiveBufferSize = size;
            return this;
        }

        public EngineConfigBuilder WithSendBufferSize(int size)
        {
            _config.SendBufferSize = size;
            return this;
        }

        public EngineConfigBuilder WithClearIdleSession(bool disable, int intervalSec, int timeoutSec)
        {
            _config.IsDisableClearIdleSession = disable;
            _config.ClearIdleSessionIntervalSec = intervalSec;
            _config.IdleSessionTimeOutSec = timeoutSec;
            return this;
        }

        public EngineConfigBuilder WithKeepAlive(bool disable, int timeSec, int intervalSec)
        {
            _config.IsDisableKeepAlive = disable;
            _config.KeepAliveTimeSec = timeSec;
            _config.KeepAliveIntervalSec = intervalSec;
            return this;
        }

        public EngineConfigBuilder WithSessionSnapshot(bool disable, int intervalSec)
        {
            _config.IsDisableSessionSnapshot = disable;
            _config.SessionsSnapshotIntervalSec = intervalSec;
            return this;
        }

        public EngineConfigBuilder WithSocketErrorLogging(bool enable)
        {
            _config.IsLogAllSocketError = enable;
            return this;
        }

        public EngineConfigBuilder AddListener(ListenerConfig listener)
        {
            _config.Listeners.Add(listener);
            return this;
        }

        public EngineConfigBuilder AddConnector(ConnectorConfig connector)
        {
            _config.Connectors.Add(connector);
            return this;
        }

        public EngineConfigBuilder WithEncryption(bool use)
        {
            _config.UseEncryption = use;
            return this;
        }

        public EngineConfigBuilder WithCompression(bool use, byte thresholdPercent)
        {
            _config.UseCompression = use;
            _config.CompressionThresholdPercent = thresholdPercent;
            return this;
        }

        public EngineConfigBuilder WithPeerUpdateInterval(int ms)
        {
            _config.PeerUpdateIntervalMs = ms;
            return this;
        }

        public EngineConfigBuilder WithConnectorUpdateInterval(int ms)
        {
            _config.ConnectorUpdateIntervalMs = ms;
            return this;
        }

        public EngineConfigBuilder WithThreadPool(int minWorker, int maxWorker, int minIo, int maxIo)
        {
            _config.MinWorkingThreads = minWorker;
            _config.MaxWorkingThreads = maxWorker;
            _config.MinCompletionPortThreads = minIo;
            _config.MaxCompletionPortThreads = maxIo;
            return this;
        }
        
        public EngineConfig Build() => _config;
    }
}
