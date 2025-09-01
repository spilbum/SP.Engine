﻿using System;
using Serilog.Events;
using SP.Common.Logging;
using ILogger = SP.Common.Logging.ILogger;

namespace SP.Engine.Server.Logging
{
    public static class ExtensionMethod
    {
        public static LogEventLevel ToSerilogLevel(this LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Info => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                LogLevel.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Debug
            };
        }
    }

    public class SerilogLogger(Serilog.ILogger logger) : ILogger
    {
        private readonly Serilog.ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        private bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel.ToSerilogLevel());

        public void Log(LogLevel level, string message)
        {
            if (!IsEnabled(level)) return;
            _logger.Write(level.ToSerilogLevel(), message);
        }

        public void Log(LogLevel level, string format, params object[] args)
        {
            if (!IsEnabled(level)) return;
            _logger.Write(level.ToSerilogLevel(), format, args);
        }

        public void Log(LogLevel level, Exception ex)
        {
            if (!IsEnabled(level)) return;
            _logger.Write(level.ToSerilogLevel(), "Exception: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
        }

        public void Log(LogLevel level, Exception ex, string format, params object[] args)
        {
            if (!IsEnabled(level)) return;
            var formatted = string.Format(format, args);
            _logger.Write(level.ToSerilogLevel(), "{Message}\nException: {Exception}\n{StackTrace}", formatted,
                ex.Message, ex.StackTrace);
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

        public void Error(Exception ex, string format, params object[] args) =>
            Log(LogLevel.Error, ex, format, args);

        public void Fatal(string message) => Log(LogLevel.Fatal, message);
        public void Fatal(string format, params object[] args) => Log(LogLevel.Fatal, format, args);
        public void Fatal(Exception ex) => Log(LogLevel.Fatal, ex);

        public void Fatal(Exception ex, string format, params object[] args) =>
            Log(LogLevel.Fatal, ex, format, args);
    }
}
