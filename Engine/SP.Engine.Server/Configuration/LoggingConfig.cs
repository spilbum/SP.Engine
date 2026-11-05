using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace SP.Engine.Server.Configuration;

public sealed class LoggingConfig
{
    public LogLevel MinimumLevel { get; set; } = GetDefaultMinimumLevel();
    public string LogDirectory { get; set; } = "logs";
    public int RollingSizeMB { get; set; } = 256;
    public bool EnableConsole { get; set; } = true;
    public bool EnableFile { get; set; } = true;

    private static LogLevel GetDefaultMinimumLevel()
    {
        var entry = Assembly.GetEntryAssembly();
        var debugAttr = entry?.GetCustomAttribute<DebuggableAttribute>();
        return debugAttr is { IsJITTrackingEnabled: true }
            ? LogLevel.Debug
            : LogLevel.Information;
    }
}
