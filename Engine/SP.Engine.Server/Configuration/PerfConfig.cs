
namespace SP.Engine.Server.Configuration;

public sealed record PerfConfig
{
    public bool MonitorEnabled { get; set; } = true;
    public int LoggingPeriodSec { get; set; } = 5;
}
