namespace SP.Engine.Server.Configuration;

public sealed record SessionConfig
{
    /// <summary>
    /// 최대 동시 접속 허용 수
    /// </summary>
    public int MaxConnections { get; init; } = 3_000;
    /// <summary>
    /// 무응답 세션 자동 정리 여부
    /// </summary>
    public bool EnableClearIdleSession { get; init; } = true;
    /// <summary>
    /// 무응답 세션 스캔 주기
    /// </summary>
    public int ClearIdleSessionPeriodSec { get; init; } = 120;
    /// <summary>
    /// 세션을 끊기까지의 최대 무응답 시간
    /// </summary>
    public int IdleSessionTimeoutSec { get; init; } = 300;

    /// <summary>
    /// 세션 목록 스냅샷 생성 여부
    /// </summary>
    public bool EnableSessionSnapshot { get; init; } = true;
    /// <summary>
    /// 스냅샷 갱신 주기
    /// </summary>
    public int SessionSnapshotPeriodSec { get; init; } = 3;
    /// <summary>
    /// 끊긴 유저의 재접속 대기 시간
    /// </summary>
    public int WaitingReconnectTimeoutSec { get; init; } = 120;
    /// <summary>
    /// 재접속 대기 타임아웃 체크 주기
    /// </summary>
    public int WaitingReconnectTimerPeriodSec { get; init; } = 60;

    /// <summary>
    /// 핸드쉐이크 대기 목록 체크 주기
    /// </summary>
    public int HandshakePendingTimerPeriodSec { get; init; } = 60;
    /// <summary>
    /// 인증 만료 타임아웃
    /// </summary>
    public int AuthHandshakeTimeoutSec { get; init; } = 120;
    /// <summary>
    /// 종료 절차 완료 타임아웃
    /// </summary>
    public int CloseHandshakeTimeoutSec { get; init; } = 120;

    /// <summary>
    /// 피어 로직 처리 주기
    /// </summary>
    public int PeerUpdateIntervalMs { get; init; } = 50;
    /// <summary>
    /// 커넥터 상태 체크 주기
    /// </summary>
    public int ConnectorUpdateIntervalMs { get; init; } = 30;
    
    /// <summary>
    /// 작업 큐 과부하로 수신을 중단할 시점
    /// </summary>
    public int PeerJobBackPressureThreshold { get; init; } = 200;
    /// <summary>
    /// 수신 중단 후 다시 수신을 재개할 큐 크기
    /// </summary>
    public int PeerJobResumeThreshold { get; init; } = 50;
    /// <summary>
    /// 느린 처리를 경고하거나 기록할 임계치
    /// </summary>
    public int PeerJobSlowThresholdMs { get; init; } = 100;
    
    /// <summary>
    /// UDP 상태 체크 주기
    /// </summary>
    public int UdpHealthCheckIntervalSec { get; init; } = 2;
    /// <summary>
    /// UDP 상태 체크 최소 타임아웃 
    /// </summary>
    public int UdpHealthCheckMinTimeoutMs { get; init; } = 500;
    /// <summary>
    /// UDP 상태 체크 최대 실패 횟수
    /// </summary>
    public int UdpHealthCheckMaxFailCount { get; init; } = 3;
}
