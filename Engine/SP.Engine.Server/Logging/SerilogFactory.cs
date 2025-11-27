using System;
using System.Collections.Concurrent;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SP.Core.Logging;
using ILogger = SP.Core.Logging.ILogger;

namespace SP.Engine.Server.Logging;

public class ThreadIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var threadId = Environment.CurrentManagedThreadId;
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadId", threadId));
    }
}

public class SerilogFactory : ILoggerFactory
{
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();
    private readonly ThreadIdEnricher _threadIdEnricher = new();
    private readonly Logger _root;

    public SerilogFactory()
    {
        Directory.CreateDirectory("logs");
        
        _root = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.With(_threadIdEnricher)
            .WriteTo.Async(a => a.Console(
                outputTemplate:
                "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [T:{ThreadId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            ), 16384, true)
            .WriteTo.Async(a => a.File(
                $"logs/app.log",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 256 * 1024 * 1024, //256MB
                retainedFileCountLimit: 7,
                shared: false,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2),
                outputTemplate:
                "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [T:{ThreadId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            ), 16384, true)
            .CreateLogger();
    }

    public ILogger GetLogger(string category)
    {
        return _loggers.GetOrAdd(category, cat =>
        {
            var logger = _root.ForContext(Constants.SourceContextPropertyName, cat);
            return new SerilogLogger(logger);
        });
    }

    public void Dispose()
    {
        if (_root is IDisposable d) d.Dispose();
    }
}
