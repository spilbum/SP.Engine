namespace SP.Engine.Client.Configuration
{
    public class EngineConfig
    {
        /// <summary>
        ///     KeepAlive 활성화 여부
        /// </summary>
        public bool EnableKeepAlive { get; set; } = true;

        /// <summary>
        ///     KeepAlive 만료 시간
        /// </summary>
        public int KeepAliveTimeSec { get; set; } = 30;

        /// <summary>
        ///     KeepAlive 주기
        /// </summary>
        public int KeepAliveIntervalSec { get; set; } = 2;

        /// <summary>
        ///     Mtu 값 (기본값: 1200)
        /// </summary>
        public ushort UdpMtu { get; set; } = 1200;

        // Udp HealthCheck 최대 실패 횟수 (기본값: 3회)
        public int MaxUdpHealthFail { get; set; } = 3;

        // UDP HealthCheck 주기 (기본값: 10초)
        public int UdpHealthCheckIntervalSec { get; set; } = 10;

        // UDP 핸드쉐이크 시간 제한 (기본값: 5초)
        public int UdpHandshakeTimeSec { get; set; } = 5;

        /// <summary>
        ///     전송 큐 사이즈 (기본값: 512개)
        /// </summary>
        public int SendQueueSize { get; set; } = 512;

        /// <summary>
        ///     전송 버퍼 크기 (기본값: 4k)
        /// </summary>
        public int SendBufferSize { get; set; } = 4 * 1024;

        /// <summary>
        ///     수신 버퍼 크기 (기본값: 64k)
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 64 * 1024;

        /// <summary>
        ///     핑 활성화 여부(기본값: true)
        /// </summary>
        public bool EnableAutoPing { get; set; } = true;

        /// <summary>
        ///     핑 간격(기본값: 30초)
        /// </summary>
        public int AutoPingIntervalSec { get; set; } = 30;

        /// <summary>
        ///     최대 연결 시도 횟수 (기본값: 2회)
        /// </summary>
        public int MaxConnectAttempts { get; set; } = 2;

        /// <summary>
        ///     연결 시도 주기 (기본값: 15초)
        /// </summary>
        public int ConnectAttemptIntervalSec { get; set; } = 15;

        /// <summary>
        ///     최대 재연결 시도 횟수 (기본값: 5회)
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        ///     재연결 주기 (기본값: 30초)
        /// </summary>
        public int ReconnectAttemptIntervalSec { get; set; } = 30;
    }

    public class EngineConfigBuilder
    {
        private readonly EngineConfig _config = new EngineConfig();

        public static EngineConfigBuilder Create()
        {
            return new EngineConfigBuilder();
        }

        public EngineConfigBuilder WithAutoPing(bool enable, int intervalSec)
        {
            _config.EnableAutoPing = enable;
            _config.AutoPingIntervalSec = intervalSec;
            return this;
        }

        public EngineConfigBuilder WithReconnectAttempt(int max, int intervalSec)
        {
            _config.MaxReconnectAttempts = max;
            _config.ReconnectAttemptIntervalSec = intervalSec;
            return this;
        }

        public EngineConfigBuilder WithUdpMtu(ushort mtu)
        {
            _config.UdpMtu = mtu;
            return this;
        }

        public EngineConfigBuilder WithKeepAlive(bool enable, int timeSec, int intervalSec)
        {
            _config.EnableKeepAlive = enable;
            _config.KeepAliveTimeSec = timeSec;
            _config.KeepAliveIntervalSec = intervalSec;
            return this;
        }

        public EngineConfigBuilder WithUdpHealthCheck(int intervalSec, int maxFailCount)
        {
            _config.UdpHealthCheckIntervalSec = intervalSec;
            _config.MaxUdpHealthFail = maxFailCount;
            return this;
        }

        public EngineConfigBuilder WithConnectAttempt(int max, int intervalSec)
        {
            _config.MaxConnectAttempts = max;
            _config.ConnectAttemptIntervalSec = intervalSec;
            return this;
        }

        public EngineConfig Build()
        {
            return _config;
        }
    }
}
