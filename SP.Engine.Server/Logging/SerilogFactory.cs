using System;
using Serilog;
using Serilog.Events;
using SP.Engine.Common.Logging;
using ILogger = SP.Engine.Common.Logging.ILogger;

namespace SP.Engine.Server.Logging
{
    public class SerilogFactory : ILoggerFactory
    {
        private const string OutputTemplate =
            "{Timestamp:yyyy-MM-dd hh:mm:ss.fff} [{Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}";
        
        public ILogger GetLogger(string name)
        {
            var loggerConfiguration = new LoggerConfiguration()
                .SetMinimumLogLevel(ELogLevel.Debug)
                .Enrich.WithProperty("ThreadId", Environment.CurrentManagedThreadId)
                .WriteTo.Console(outputTemplate: OutputTemplate);

            loggerConfiguration.WriteTo.File(
                path: $"logs/error-{name}-.log",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Error,
                outputTemplate: OutputTemplate
            );
            
            loggerConfiguration.WriteTo.File(
                path: $"logs/{name}-.log",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: OutputTemplate
            );

            var logger = loggerConfiguration.CreateLogger();
            return new SerilogLogger(logger);
        }
    }
}
