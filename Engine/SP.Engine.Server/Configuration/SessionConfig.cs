namespace SP.Engine.Server.Configuration;

public sealed class SessionConfig
{
    /// <summary>
    /// 최대 동시 접속 허용 수
    /// </summary>
    public int MaxConnections { get; set; } = 3_000;
    /// <summary>
    /// 무응답 세션 자동 정리 여부
    /// </summary>
    public bool EnableClearIdleSession { get; set; } = true;
    /// <summary>
    /// 무응답 세션 스캔 주기
    /// </summary>
    public int ClearIdleSessionPeriodSec { get; set; } = 120;
    /// <summary>
    /// 세션을 끊기까지의 최대 무응답 시간
    /// </summary>
    public int IdleSessionTimeoutSec { get; set; } = 300;

    /// <summary>
    /// 끊긴 유저의 재접속 대기 시간
    /// </summary>
    public int WaitingReconnectTimeoutSec { get; set; } = 120;
    /// <summary>
    /// 재접속 대기 타임아웃 체크 주기
    /// </summary>
    public int WaitingReconnectTimerPeriodSec { get; set; } = 60;

    /// <summary>
    /// 핸드쉐이크 대기 목록 체크 주기
    /// </summary>
    public int HandshakePendingTimerPeriodSec { get; set; } = 60;
    /// <summary>
    /// 인증 만료 타임아웃
    /// </summary>
    public int AuthHandshakeTimeoutSec { get; set; } = 120;
    /// <summary>
    /// 종료 절차 완료 타임아웃
    /// </summary>
    public int CloseHandshakeTimeoutSec { get; set; } = 120;

    /// <summary>
    /// 피어 로직 처리 주기
    /// </summary>
    public int LogicTickIntervalMs { get; set; } = 50;
    /// <summary>
    /// 서버 연결 클라이언트 상태 체크 주기
    /// </summary>
    public int S2SClientUpdateIntervalMs { get; set; } = 30;
    public int CommandSlowThresholdMs { get; set; } = 100;
    
    /// <summary>
    /// UDP 상태 체크 주기
    /// </summary>
    public int UdpHealthCheckIntervalSec { get; set; } = 2;
    /// <summary>
    /// UDP 상태 체크 최소 타임아웃 
    /// </summary>
    public int UdpHealthCheckMinTimeoutMs { get; set; } = 500;
    /// <summary>
    /// UDP 상태 체크 최대 실패 횟수
    /// </summary>
    public int UdpHealthCheckMaxFailCount { get; set; } = 3;
}
