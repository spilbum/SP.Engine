namespace SP.Engine.Client.Configuration
{
    public class EngineConfig
    {
        /// <summary>
        /// 자동 핑 비활성화 여부(기본값: false)
        /// </summary>
        public bool IsDisableAutoPing { get; set; }
        /// <summary>
        /// 자동 핑 간격(기본값: 30초)
        /// </summary>
        public int AutoPingIntervalSec { get; set; } = 30;
        /// <summary>
        /// 재연결 최대 시도 횟수(기본값: 5회)
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 5;
        /// <summary>
        /// 재연결 시도 간격(기본값: 30초)
        /// </summary>
        public int ReconnectAttemptIntervalSec { get; set; } = 30;
        /// <summary>
        /// MTU 값(기본값: 1200)
        /// Udp 데이터의 조각화를 위해 사용함
        /// </summary>
        public ushort UdpMtu { get; set; } = 1200;
        /// <summary>
        /// Udp KeepAlive 비활성화 여부(기본값: false)
        /// </summary>
        public bool IsDisableUdpKeepAlive { get; set; }
        /// <summary>
        /// Udp KeepAlive 주기(기본값: 30초)
        /// NAT 매핑 유지를 위해 일정 주기로 전송함
        /// </summary>
        public int UdpKeepAliveIntervalSec { get; set; } = 30;
        /// <summary>
        /// 최초 연결 최대 시도 횟수(기본값: 5회)
        /// </summary>
        public int MaxConnectAttempts { get; set; } = 2;
        /// <summary>
        /// 최초 연결 시도 간격(기본값: 15초)
        /// </summary>
        public int ConnectAttemptIntervalSec { get; set; } = 15;
        /// <summary>
        /// 레이턴시 측정을 위한 샘플 개수 (기본값: 20개)
        /// </summary>
        public int LatencySampleWindowSize { get; set; } = 20;
    }

    public class EngineConfigBuilder
    {
        private readonly EngineConfig _config = new EngineConfig();

        public static EngineConfigBuilder Create() => new EngineConfigBuilder();

        public EngineConfigBuilder WithAutoPing(bool disable, int intervalSec)
        {
            _config.IsDisableAutoPing = disable;
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

        public EngineConfigBuilder WithUdpKeepAlive(bool disable, int intervalSec)
        {
            _config.IsDisableUdpKeepAlive = disable;
            _config.UdpKeepAliveIntervalSec = intervalSec;
            return this;
        }

        public EngineConfigBuilder WithConnectAttempt(int max, int intervalSec)
        {
            _config.MaxConnectAttempts = max;
            _config.ConnectAttemptIntervalSec = intervalSec;
            return this;
        }

        public EngineConfigBuilder WithLatencySampleWindowSize(int windowSize)
        {
            _config.LatencySampleWindowSize = windowSize;
            return this;
        }

        public EngineConfig Build() => _config;
    }
}
