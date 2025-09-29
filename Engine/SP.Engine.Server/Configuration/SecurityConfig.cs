using SP.Engine.Runtime.Channel;

namespace SP.Engine.Server.Configuration;

public sealed record SecurityConfig
{
    public bool UseEncrypt { get; init; } = true;
    public bool UseCompress { get; init; } = false;
    public ushort CompressionThreshold { get; init; } = 2048;
}
