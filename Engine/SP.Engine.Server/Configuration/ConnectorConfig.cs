namespace SP.Engine.Server.Configuration;

public sealed record ConnectorConfig
{
    public string Name { get; init; }
    public string Host { get; init; }
    public int Port { get; init; }
    public int MaxConnectAttempts { get; init; } = 2;
    public int ConnectAttemptIntervalSec { get; init; } = 5;
    public int MaxReconnectAttempts { get; init; } = 5;
    public int ReconnectAttemptIntervalSec { get; init; } = 15;
    public bool EnableAutoPing { get; init; } = true;
    public int AutoPingIntervalSec { get; init; } = 2;
}
