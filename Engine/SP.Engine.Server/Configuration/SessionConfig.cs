namespace SP.Engine.Server.Configuration;

public sealed record SessionConfig
{
    public bool EnableClearIdleSession { get; init; } = true;
    public int ClearIdleSessionIntervalSec { get; init; } = 120;
    public int IdleSessionTimeoutSec { get; init; } = 300;

    public bool EnableSessionSnapshot { get; init; } = true;
    public int SessionSnapshotIntervalSec { get; init; } = 1;

    public int WaitingReconnectTimeoutSec { get; init; } = 120;
    public int WaitingReconnectTimerIntervalSec { get; init; } = 60;

    public int HandshakePendingTimerIntervalSec { get; init; } = 60;
    public int AuthHandshakeTimeoutSec { get; init; } = 120;
    public int CloseHandshakeTimeoutSec { get; init; } = 120;

    public int PeerUpdateIntervalMs { get; init; } = 50;
    public int ConnectorUpdateIntervalMs { get; init; } = 30;

    public int FragmentAssemblerCleanupTimeoutSec { get; init; } = 15;
}
