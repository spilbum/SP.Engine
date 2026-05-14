namespace SP.Engine.Server.Configuration;

public sealed record ConnectorConfig
{
    /// <summary>
    /// 커넥터 식별자
    /// </summary>
    public string Name { get; init; }
    /// <summary>
    /// 대상 서버 주소
    /// </summary>
    public string Host { get; init; }
    /// <summary>
    /// 대상 서버 포트
    /// </summary>
    public int Port { get; init; }
    
    /// <summary>
    /// 최대 초기 연결 시도 횟수
    /// </summary>
    public int MaxConnectAttempts { get; init; } = 2;
    /// <summary>
    /// 연결 시도 간격
    /// </summary>
    public int ConnectAttemptIntervalSec { get; init; } = 5;
    
    /// <summary>
    /// 최대 재연결 시도 횟수
    /// </summary>
    public int MaxReconnectAttempts { get; init; } = 5;
    /// <summary>
    /// 재연결 시도 간격
    /// </summary>
    public int ReconnectAttemptIntervalSec { get; init; } = 15;
    
    /// <summary>
    /// 자동 핑 전송 여부
    /// </summary>
    public bool EnableAutoPing { get; init; } = true;
    /// <summary>
    /// 핑 전송 주기
    /// </summary>
    public int AutoPingIntervalSec { get; init; } = 2;
}
