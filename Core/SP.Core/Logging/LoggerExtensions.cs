using System;
using Microsoft.Extensions.Logging;

namespace SP.Core.Logging
{
    public static class LoggerExtensions
    {
        public static void Debug(this ILogger logger, string message)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Debug)) return;
            logger.LogDebug(message);
        }

        public static void Debug(this ILogger logger, string format, params object[] args)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Debug)) return;
            logger.LogDebug(format, args);
        }

        public static void Info(this ILogger logger, string message)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Information)) return;
            logger.LogInformation(message);
        }

        public static void Info(this ILogger logger, string format, params object[] args)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Information)) return;
            logger.LogInformation(format, args);
        }

        public static void Warn(this ILogger logger, string message)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Warning)) return;
            logger.LogWarning(message);
        }

        public static void Warn(this ILogger logger, string format, params object[] args)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Warning)) return;
            logger.LogWarning(format, args);
        }

        public static void Error(this ILogger logger, string message)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Error)) return;
            logger.LogError(message);
        }

        public static void Error(this ILogger logger, string format, params object[] args)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Error)) return;
            logger.LogError(format, args);
        }

        public static void Error(this ILogger logger, Exception ex)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Error)) return;
            logger.LogError(ex, ex?.Message ?? string.Empty);
        }

        public static void Error(this ILogger logger, Exception ex, string message)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Error)) return;
            logger.LogError(ex, message);
        }

        public static void Error(this ILogger logger, Exception ex, string format, params object[] args)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Error)) return;
            logger.LogError(ex, format, args);
        }

        public static void Fatal(this ILogger logger, string message)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Critical)) return;
            logger.LogCritical(message);
        }

        public static void Fatal(this ILogger logger, string format, params object[] args)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Critical)) return;
            logger.LogCritical(format, args);
        }

        public static void Fatal(this ILogger logger, Exception ex)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Critical)) return;
            logger.LogCritical(ex, ex?.Message ?? string.Empty);
        }

        public static void Fatal(this ILogger logger, Exception ex, string message)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Critical)) return;
            logger.LogCritical(ex, message);
        }

        public static void Fatal(this ILogger logger, Exception ex, string format, params object[] args)
        {
            if (logger == null || !logger.IsEnabled(LogLevel.Critical)) return;
            logger.LogCritical(ex, format, args);
        }
    }
}
