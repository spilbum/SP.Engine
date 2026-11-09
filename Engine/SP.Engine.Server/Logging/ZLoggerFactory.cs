using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SP.Engine.Server.Configuration;
using ZLogger;
using ZLogger.Providers;

namespace SP.Engine.Server.Logging;

public static class ZLoggerFactory
{
    public static ILoggerFactory Create(string defaultCategory, LoggingConfig config = null)
    {
        config ??= new LoggingConfig();

        var prefixName = string.IsNullOrWhiteSpace(defaultCategory) ? "app" : defaultCategory;
        var logDir = string.IsNullOrWhiteSpace(config.LogDirectory) ? "logs" : config.LogDirectory;

        if (config.EnableFile)
        {
            Directory.CreateDirectory(logDir);
        }

        return LoggerFactory.Create(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(config.MinimumLevel);

            if (config.EnableFile)
            {
                var rollSizeKB = Math.Max(1, config.RollingSizeMB) * 1024;
                logging.AddZLoggerRollingFile(options =>
                {
                    options.FilePathSelector = (timestamp, sequenceNumber) =>
                        $"{logDir}/{prefixName}_{timestamp.ToLocalTime():yyyy-MM-dd}_{sequenceNumber:000}.log";
                    options.RollingInterval = RollingInterval.Day;
                    options.RollingSizeKB = rollSizeKB;
                    ConfigurePlainTextFormatter(options);
                });
            }
            
            if (config.EnableConsole)
            {
                logging.AddZLoggerConsole(ConfigurePlainTextFormatter);
            }
        });
    }

    private static void ConfigurePlainTextFormatter(ZLoggerOptions options)
    {
        options.UsePlainTextFormatter(formatter =>
        {
            formatter.SetPrefixFormatter(
                $"[{0:local-timeonly} {1:short}] [T:{2}] [{3}] ",
                (in MessageTemplate template, in LogInfo info) =>
                    template.Format(
                        info.Timestamp,
                        info.LogLevel,
                        Environment.CurrentManagedThreadId,
                        info.Category));
        });
    }
}
