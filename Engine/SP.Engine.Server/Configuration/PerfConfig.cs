
namespace SP.Engine.Server.Configuration;

public sealed record PerfConfig
{
    public bool MonitorEnabled { get; init; } = true;
    public bool LoggerEnabled { get; init; } = true;
    public int SamplePeriodSec { get; init; } = 1;
    public int LoggingPeriodSec { get; init; } = 5;
}
