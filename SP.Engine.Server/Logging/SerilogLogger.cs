using System;
using System.Diagnostics;
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
            switch (logLevel)
            {
                case ELogLevel.Debug:
                    return configuration.MinimumLevel.Debug();
                case ELogLevel.Info:
                    return configuration.MinimumLevel.Information();
                case ELogLevel.Warning:
                    return configuration.MinimumLevel.Warning();
                case ELogLevel.Error:
                    return configuration.MinimumLevel.Error();
                case ELogLevel.Fatal:
                    return configuration.MinimumLevel.Fatal();
                default:
                    return configuration.MinimumLevel.Debug();
            }
        }

        public static LogEventLevel ToSerilogLevel(this ELogLevel logLevel)
        {
            switch (logLevel)
            {
                case ELogLevel.Debug:
                    return LogEventLevel.Debug;
                case ELogLevel.Info:
                    return LogEventLevel.Information;
                case ELogLevel.Warning:
                    return LogEventLevel.Warning;
                case ELogLevel.Error:
                    return LogEventLevel.Error;
                case ELogLevel.Fatal:
                    return LogEventLevel.Fatal;
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, "Invalid log level");
            }
        }
    }
    
    /// <summary>
    /// Serilog 기반의 ILogger 구현
    /// </summary>
    public class SerilogLogger : ILogger
    {
        private const string ExceptionFormat = "An exception occurred: {0}{1}stackTrace={2}";
        private const string MessageAndExceptionFormat = "An exception occurred: {0}, exception={1}{2}stackTrace={3}";
        private readonly Serilog.ILogger _logger;

        public SerilogLogger(Serilog.ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private bool IsEnabled(ELogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel.ToSerilogLevel());
        }

        public void WriteLog(ELogLevel logLevel, string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
                return;

            if (!IsEnabled(logLevel))
                return;

            _logger.Write(logLevel.ToSerilogLevel(), format, args);
        }

        public void WriteLog(Exception ex)
        {
            if (!IsEnabled(ELogLevel.Error))
                return;
            
            _logger.Write(LogEventLevel.Error, ExceptionFormat, ex.Message, Environment.NewLine, ex.StackTrace);
        }

        public void WriteLog(string message, Exception ex)
        {
            if (!IsEnabled(ELogLevel.Error))
                return;
            
            _logger.Write(LogEventLevel.Error, MessageAndExceptionFormat, message, ex.Message, Environment.NewLine, ex.StackTrace);
        }
    }
}
