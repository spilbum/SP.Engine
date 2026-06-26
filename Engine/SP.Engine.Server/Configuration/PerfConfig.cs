
namespace SP.Engine.Server.Configuration;

public sealed class PerfConfig
{
    public bool MonitorEnabled { get; set; } = true;
    public int LoggingPeriodSec { get; set; } = 5;
}
