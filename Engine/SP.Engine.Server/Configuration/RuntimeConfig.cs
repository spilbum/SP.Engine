using System;

namespace SP.Engine.Server.Configuration;

public sealed record RuntimeConfig
{
    public int MinWorkerThreads { get; init; } = Environment.ProcessorCount;
    public int MaxWorkerThreads { get; init; } = Math.Max(Environment.ProcessorCount * 8, 64);
    public int MinIoThreads { get; init; } = Environment.ProcessorCount;
    public int MaxIoThreads { get; init; } = Math.Max(Environment.ProcessorCount * 8, 64);

    public bool PerfMonitorEnabled { get; init; } = true;
    public bool PrefLoggerEnabled { get; init; } = true;
    public TimeSpan PerfSamplePeriod { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan PerfLoggingPeriod { get; init; } = TimeSpan.FromSeconds(5);
}
