using System;
using System.Collections.Generic;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SP.Engine.Common.Logging;
using ILogger = SP.Engine.Common.Logging.ILogger;

namespace SP.Engine.Server.Logging
{
    public class ThreadIdEnricher : Serilog.Core.ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var threadId = Environment.CurrentManagedThreadId;
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadId", threadId));
        }
    }
    
    public class SerilogFactory : ILoggerFactory
    {
        private readonly Dictionary<string, ILogger> _loggers = new();
        private readonly object _lock = new();

        public ILogger GetLogger(string category)
        {
            lock (_lock)
            {
                if (_loggers.TryGetValue(category, out var existing))
                    return existing;

                var logger = new LoggerConfiguration()
                    .Enrich.With(new ThreadIdEnricher())
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        path: $"logs/{category}.log",
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [T:{ThreadId}] {Message:lj}{NewLine}{Exception}"
                    )
                    .CreateLogger();

                var wrapper = new SerilogLogger(logger.ForContext("Category", category));
                _loggers[category] = wrapper;
                return wrapper;
            }
        }
    }
}
