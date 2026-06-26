namespace SP.Engine.Client.Configuration
{
    public class EngineConfig
    {
        /// <summary>
        /// KeepAlive 활성화 여부 (기본값:true)
        /// </summary>
        public bool EnableKeepAlive { get; set; } = true;

        /// <summary>
        /// KeepAlive 만료 시간 (기본값:30초)
        /// </summary>
        public uint KeepAliveTimeSec { get; set; } = 30;

        /// <summary>
        /// KeepAlive 주기 (기본값:2초)
        /// </summary>
        public uint KeepAliveIntervalSec { get; set; } = 2;

        /// <summary>
        /// Mtu 값 (기본값: 1200)
        /// </summary>
        public ushort UdpMtu { get; set; } = 1200;

        /// <summary>
        /// Udp HealthCheck 최대 실패 횟수 (기본값: 3회)
        /// </summary>
        public int UdpHealthCheckThreshold { get; set; } = 3;

        /// <summary>
        /// UDP HealthCheck 주기 (기본값: 10초)
        /// </summary>
        public int UdpHealthCheckIntervalSec { get; set; } = 10;

        /// <summary>
        /// UDP 핸드쉐이크 시간 제한 (기본값: 5초)
        /// </summary>
        public int UdpHandshakeTimeSec { get; set; } = 5;

        /// <summary>
        /// 전송 버퍼 크기 (기본값: 4k)
        /// </summary>
        public int SendBufferSize { get; set; } = 4 * 1024;

        /// <summary>
        /// 수신 버퍼 크기 (기본값: 64k)
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 64 * 1024;
        
        /// <summary>
        /// 핑 활성화 여부 (기본값:true)
        /// </summary>
        public bool EnableAutoPing { get; set; } = true;
        /// <summary>
        /// 핑 간격(기본값: 30초)
        /// </summary>
        public int AutoPingIntervalSec { get; set; } = 30;

        /// <summary>
        /// 최대 연결 시도 횟수 (기본값: 2회)
        /// </summary>
        public int MaxConnectAttempts { get; set; } = 2;

        /// <summary>
        /// 연결 시도 주기 (기본값: 15초)
        /// </summary>
        public int ConnectAttemptIntervalSec { get; set; } = 15;

        /// <summary>
        /// 최대 재연결 시도 횟수 (기본값: 5회)
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        /// 재연결 주기 (기본값: 30초)
        /// </summary>
        public int ReconnectAttemptIntervalSec { get; set; } = 30;
    }
}
