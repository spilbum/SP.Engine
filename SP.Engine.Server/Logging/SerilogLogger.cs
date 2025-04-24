using System;
using Serilog;
using Serilog.Events;
using SP.Engine.Common.Logging;
using ILogger = SP.Engine.Common.Logging.ILogger;

namespace SP.Engine.Server.Logging
{
      public static class ExtensionMethod
    {
        public static LoggerConfiguration SetMinimumLogLevel(this LoggerConfiguration configuration, ELogLevel logLevel)
        {
            return logLevel switch
            {
                ELogLevel.Debug => configuration.MinimumLevel.Debug(),
                ELogLevel.Info => configuration.MinimumLevel.Information(),
                ELogLevel.Warning => configuration.MinimumLevel.Warning(),
                ELogLevel.Error => configuration.MinimumLevel.Error(),
                ELogLevel.Fatal => configuration.MinimumLevel.Fatal(),
                _ => configuration.MinimumLevel.Debug(),
            };
        }

        public static LogEventLevel ToSerilogLevel(this ELogLevel logLevel)
        {
            return logLevel switch
            {
                ELogLevel.Debug => LogEventLevel.Debug,
                ELogLevel.Info => LogEventLevel.Information,
                ELogLevel.Warning => LogEventLevel.Warning,
                ELogLevel.Error => LogEventLevel.Error,
                ELogLevel.Fatal => LogEventLevel.Fatal,
                _ => LogEventLevel.Debug
            };
        }
    }

    public class SerilogLogger(Serilog.ILogger logger) : ILogger
    {
        private readonly Serilog.ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        private bool IsEnabled(ELogLevel logLevel) => _logger.IsEnabled(logLevel.ToSerilogLevel());

        public void Log(ELogLevel level, string message)
        {
            if (!IsEnabled(level)) return;
            _logger.Write(level.ToSerilogLevel(), message);
        }

        public void Log(ELogLevel level, string format, params object[] args)
        {
            if (!IsEnabled(level)) return;
            _logger.Write(level.ToSerilogLevel(), format, args);
        }

        public void Log(ELogLevel level, Exception ex)
        {
            if (!IsEnabled(level)) return;
            _logger.Write(level.ToSerilogLevel(), "Exception: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
        }

        public void Log(ELogLevel level, Exception ex, string format, params object[] args)
        {
            if (!IsEnabled(level)) return;
            var formatted = string.Format(format, args);
            _logger.Write(level.ToSerilogLevel(), "{Message}\nException: {Exception}\n{StackTrace}", formatted,
                ex.Message, ex.StackTrace);
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

        public void Error(Exception ex, string format, params object[] args) =>
            Log(ELogLevel.Error, ex, format, args);

        public void Fatal(string message) => Log(ELogLevel.Fatal, message);
        public void Fatal(string format, params object[] args) => Log(ELogLevel.Fatal, format, args);
        public void Fatal(Exception ex) => Log(ELogLevel.Fatal, ex);

        public void Fatal(Exception ex, string format, params object[] args) =>
            Log(ELogLevel.Fatal, ex, format, args);
    }
}
