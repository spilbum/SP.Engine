namespace SP.Engine.Server.Configuration;

public sealed record ListenerConfig
{
    public string Ip { get; init; } = "Any";
    public int Port { get; init; }
    public int BackLog { get; init; } = 1024;
    public SocketMode Mode { get; init; }
}
