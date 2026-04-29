namespace SP.Engine.Server.Configuration;

public sealed record SessionConfig
{
    public int MaxConnections { get; init; } = 3_000;
    public bool EnableClearIdleSession { get; init; } = true;
    public int ClearIdleSessionIntervalSec { get; init; } = 120;
    public int IdleSessionTimeoutSec { get; init; } = 300;

    public bool EnableSessionSnapshot { get; init; } = true;
    public int SessionSnapshotIntervalSec { get; init; } = 3;

    public int WaitingReconnectTimeoutSec { get; init; } = 120;
    public int WaitingReconnectTimerIntervalSec { get; init; } = 60;

    public int HandshakePendingTimerIntervalSec { get; init; } = 60;
    public int AuthHandshakeTimeoutSec { get; init; } = 120;
    public int CloseHandshakeTimeoutSec { get; init; } = 120;

    public int PeerUpdateIntervalMs { get; init; } = 50;
    public int ConnectorTickPeriodMs { get; init; } = 30;
    
    // 백프레셔가 발동되는 임계치(수신 중단)
    public int PeerJobBackPressureThreshold { get; init; } = 200;
    // 작업이 처리되어 수신이 재개되는 시점
    public int PeerJobResumeThreshold { get; init; } = 50;
}
