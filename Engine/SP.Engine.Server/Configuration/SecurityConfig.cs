namespace SP.Engine.Server.Configuration;

public sealed record SecurityConfig
{
    public bool UseEncryption { get; init; } = true;
    public bool UseCompression { get; init; } = true;
    public byte CompressionThresholdPercent { get; init; } = 20;
}
