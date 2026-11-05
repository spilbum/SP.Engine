using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SP.Core.Logging
{
    public class ConsoleLogger : ILogger
    {
        private readonly LogLevel _minLevel;
        private readonly string _category;
        private readonly IReadOnlyDictionary<string, object> _context;

        public ConsoleLogger(string category, LogLevel minLevel = LogLevel.Debug, IReadOnlyDictionary<string, object> context = null)
        {
            _category = category;
            _minLevel = minLevel;
            _context = context;
        }

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None && logLevel >= _minLevel;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            if (formatter == null) throw new ArgumentNullException(nameof(formatter));

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception == null) return;

            var time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var ctx = _context != null && _context.Count > 0
                ? " | " + string.Join(" ", _context.Select(kv => $"{kv.Key}={kv.Value}"))
                : string.Empty;

            var text = exception != null
                ? string.IsNullOrEmpty(message)
                    ? $"An exception occurred: {exception.Message}\r\n{exception.StackTrace}"
                    : $"{message}\nAn exception occurred: {exception.Message}\r\n{exception.StackTrace}"
                : message;

            Console.WriteLine($"[{time}][{_category}][{GetLevelShortName(logLevel)}] {text}{ctx}");
        }

        private static string GetLevelShortName(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRI",
                _ => level.ToString().ToUpperInvariant()
            };
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }
}
