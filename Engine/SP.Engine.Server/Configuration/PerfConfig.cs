using System;

namespace SP.Engine.Server.Configuration;

public sealed record PerfConfig
{
    public bool MonitorEnabled { get; init; } = true;
    public bool LoggerEnabled { get; init; } = true;
    public TimeSpan SamplePeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan LoggingPeriod { get; init; } = TimeSpan.FromSeconds(5);
}
