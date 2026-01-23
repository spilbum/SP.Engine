using System;
using System.Collections.Generic;
using System.Linq;

namespace SP.Core.Logging
{
    public class ConsoleLogger : ILogger
    {
        private readonly string _category;
        private readonly IReadOnlyDictionary<string, object> _context;

        public ConsoleLogger(string category, IReadOnlyDictionary<string, object> context = null)
        {
            _category = category;
            _context = context;
        }

        public ILogger With(string key, object value)
        {
            var dict = _context == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(_context);

            dict[key] = value;
            return new ConsoleLogger(_category, dict);
        }

        public void Log(LogLevel level, string message)
        {
            Write(level, message);
        }

        public void Log(LogLevel level, string format, params object[] args)
        {
            Write(level, string.Format(format, args));
        }

        public void Log(LogLevel level, Exception ex)
        {
            Write(level, $"An exception occurred: {ex.Message}\r\n{ex.StackTrace}");
        }

        public void Log(LogLevel level, Exception ex, string format, params object[] args)
        {
            Write(level, string.Format(format, args) + "\n" +
                         $"An exception occurred: {ex.Message}\r\n{ex.StackTrace}");
        }

        public void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public void Debug(string format, params object[] args)
        {
            Log(LogLevel.Debug, format, args);
        }

        public void Info(string message)
        {
            Log(LogLevel.Info, message);
        }

        public void Info(string format, params object[] args)
        {
            Log(LogLevel.Info, format, args);
        }

        public void Warn(string message)
        {
            Log(LogLevel.Warning, message);
        }

        public void Warn(string format, params object[] args)
        {
            Log(LogLevel.Warning, format, args);
        }

        public void Error(string message)
        {
            Log(LogLevel.Error, message);
        }

        public void Error(string format, params object[] args)
        {
            Log(LogLevel.Error, format, args);
        }

        public void Error(Exception ex)
        {
            Log(LogLevel.Error, ex);
        }

        public void Error(Exception ex, string format, params object[] args)
        {
            Log(LogLevel.Error, ex, format, args);
        }

        public void Fatal(string message)
        {
            Log(LogLevel.Fatal, message);
        }

        public void Fatal(string format, params object[] args)
        {
            Log(LogLevel.Fatal, format, args);
        }

        public void Fatal(Exception ex)
        {
            Log(LogLevel.Fatal, ex);
        }

        public void Fatal(Exception ex, string format, params object[] args)
        {
            Log(LogLevel.Fatal, ex, format, args);
        }

        private void Write(LogLevel level, string message)
        {
            var time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var ctx = _context != null && _context.Count > 0
                ? " | " + string.Join(" ", _context.Select(kv => $"{kv.Key}={kv.Value}"))
                : string.Empty;

            Console.WriteLine($"[{time}][{_category}][{level.ToString().ToUpper()}] {message}{ctx}");
        }
    }
}
