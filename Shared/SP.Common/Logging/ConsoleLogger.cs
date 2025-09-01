
using System;
using System.Collections.Generic;

namespace SP.Common.Logging
{
    public class ConsoleLogger : ILogger
    {
        private readonly string _category;
        private readonly object _lock = new object();

        public ConsoleLogger(string category)
        {
            _category = category;
        }

        public void Log(LogLevel level, string message)
            => Write(level, message);

        public void Log(LogLevel level, string format, params object[] args)
            => Write(level, string.Format(format, args));

        public void Log(LogLevel level, Exception ex)
            => Write(level, ex.ToString());

        public void Log(LogLevel level, Exception ex, string format, params object[] args)
            => Write(level, string.Format(format, args) + "\n" + ex);

        private void Write(LogLevel level, string message)
        {
            lock (_lock)
            {
                Console.WriteLine("[{0:yyyy-MM-dd hh:mm:ss.fff}][{1}][{2}] {3}", DateTime.UtcNow, _category, level.ToString().ToUpper(), message);
            }
        }

        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Debug(string format, params object[] args) => Log(LogLevel.Debug, format, args);

        public void Info(string message) => Log(LogLevel.Info, message);
        public void Info(string format, params object[] args) => Log(LogLevel.Info, format, args);

        public void Warn(string message) => Log(LogLevel.Warning, message);
        public void Warn(string format, params object[] args) => Log(LogLevel.Warning, format, args);

        public void Error(string message) => Log(LogLevel.Error, message);
        public void Error(string format, params object[] args) => Log(LogLevel.Error, format, args);
        public void Error(Exception ex) => Log(LogLevel.Error, ex);
        public void Error(Exception ex, string format, params object[] args) => Log(LogLevel.Error, ex, format, args);

        public void Fatal(string message) => Log(LogLevel.Fatal, message);
        public void Fatal(string format, params object[] args) => Log(LogLevel.Fatal, format, args);
        public void Fatal(Exception ex) => Log(LogLevel.Fatal, ex);
        public void Fatal(Exception ex, string format, params object[] args) => Log(LogLevel.Fatal, ex, format, args);

    }

}
