namespace SP.Engine.Server.Configuration;

public sealed class S2SClientConfig
{
    /// <summary>
    /// 커넥터 식별자
    /// </summary>
    public string Name { get; set; }
    /// <summary>
    /// 대상 서버 주소
    /// </summary>
    public string Host { get; set; }
    /// <summary>
    /// 대상 서버 포트
    /// </summary>
    public int Port { get; set; }
    
    /// <summary>
    /// 최대 초기 연결 시도 횟수
    /// </summary>
    public int MaxConnectAttempts { get; set; } = 2;
    /// <summary>
    /// 연결 시도 간격
    /// </summary>
    public int ConnectAttemptIntervalSec { get; set; } = 5;
    
    /// <summary>
    /// 최대 재연결 시도 횟수
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;
    /// <summary>
    /// 재연결 시도 간격
    /// </summary>
    public int ReconnectAttemptIntervalSec { get; set; } = 15;
    
    /// <summary>
    /// 자동 핑 전송 여부
    /// </summary>
    public bool EnableAutoPing { get; set; } = true;
    /// <summary>
    /// 핑 전송 주기
    /// </summary>
    public int AutoPingIntervalSec { get; set; } = 2;
    /// <summary>
    /// 업데이트 주기
    /// </summary>
    public int UpdateIntervalMs { get; set; } = 30;
}
