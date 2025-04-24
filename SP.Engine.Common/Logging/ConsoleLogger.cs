
using System;
using System.Collections.Generic;

namespace SP.Engine.Common.Logging
{
    public class ConsoleLogger : ILogger
    {
        private readonly string _category;
        private readonly object _lock = new object();

        public ConsoleLogger(string category)
        {
            _category = category;
        }

        public void Log(ELogLevel level, string message)
            => Write(level, message);

        public void Log(ELogLevel level, string format, params object[] args)
            => Write(level, string.Format(format, args));

        public void Log(ELogLevel level, Exception ex)
            => Write(level, ex.ToString());

        public void Log(ELogLevel level, Exception ex, string format, params object[] args)
            => Write(level, string.Format(format, args) + "\n" + ex);

        private void Write(ELogLevel level, string message)
        {
            lock (_lock)
            {
                Console.WriteLine("{0}-{1}: {2}", _category, level.ToString().ToUpper(), message);
            }
        }

        public void Debug(string message) => Log(ELogLevel.Debug, message);
        public void Debug(string format, params object[] args) => Log(ELogLevel.Debug, format, args);

        public void Info(string message) => Log(ELogLevel.Info, message);
        public void Info(string format, params object[] args) => Log(ELogLevel.Info, format, args);

        public void Warn(string message) => Log(ELogLevel.Warning, message);
        public void Warn(string format, params object[] args) => Log(ELogLevel.Warning, format, args);

        public void Error(string message) => Log(ELogLevel.Error, message);
        public void Error(string format, params object[] args) => Log(ELogLevel.Error, format, args);
        public void Error(Exception ex) => Log(ELogLevel.Error, ex);
        public void Error(Exception ex, string format, params object[] args) => Log(ELogLevel.Error, ex, format, args);

        public void Fatal(string message) => Log(ELogLevel.Fatal, message);
        public void Fatal(string format, params object[] args) => Log(ELogLevel.Fatal, format, args);
        public void Fatal(Exception ex) => Log(ELogLevel.Fatal, ex);
        public void Fatal(Exception ex, string format, params object[] args) => Log(ELogLevel.Fatal, ex, format, args);

    }

}
