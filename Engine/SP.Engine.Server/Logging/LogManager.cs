using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SP.Common.Logging;
using SP.Common.Fiber;

namespace SP.Engine.Server.Logging
{
    public static class LogManager
    {
        private static ILoggerFactory _loggerFactory;
        private static readonly ConcurrentDictionary<string, ILogger> Loggers = new();
        private static readonly List<ThreadFiber> Fibers = [];
        private static int _fiberIndex;
        private static string _defaultCategory;

        public static void Initialize(string defaultCategory, ILoggerFactory loggerFactory)
        {
            _defaultCategory = defaultCategory;
            _loggerFactory = loggerFactory;

            var logger = GetLogger(defaultCategory);
            for (var i = 0; i < Environment.ProcessorCount; i++)
            {
                var fiber = new ThreadFiber(logger);
                fiber.Start();
                Fibers.Add(fiber);
            }
        }
        public static ILogger GetLogger(string category = null)
        {
            if (string.IsNullOrEmpty(category))
                category = _defaultCategory ?? throw new ArgumentNullException(nameof(category));
            return Loggers.GetOrAdd(category, key => _loggerFactory.GetLogger(key));
        }

        private static void Enqueue(Action job)
        {
            var index = Interlocked.Increment(ref _fiberIndex);
            var fiber = Fibers[index % Fibers.Count];
            fiber.Enqueue(job);
        }

        public static void Dispose()
        {
            foreach (var fiber in Fibers)
                fiber.Dispose();
            
            try
            {
                Serilog.Log.CloseAndFlush();
            }
            catch (Exception)
            {
                // Serilog가 없거나 예외 발생 시 무시
            }
        }

        private static void Log(LogLevel level, string category, string message)
        {
            var logger = GetLogger(category);
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => logger.Log(level, prefix + message));
        }

        private static void Log(LogLevel level, string category, string format, params object[] args)
        {
            var logger = GetLogger(category);
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => logger.Log(level, prefix + string.Format(format, args)));
        }

        private static void Log(LogLevel level, ILogContext context, string format, params object[] args)
        {
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => context.Logger.Log(level, prefix + string.Format(format, args)));
        }

        private static void LogDefault(LogLevel level, string format, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(_defaultCategory))
                throw new InvalidOperationException("Default log category is not set.");
            Log(level, _defaultCategory, format, args);
        }

        public static void Debug(string category, string format, params object[] args) =>
            Log(LogLevel.Debug, category, format, args);

        public static void Info(string category, string format, params object[] args) =>
            Log(LogLevel.Info, category, format, args);

        public static void Warn(string category, string format, params object[] args) =>
            Log(LogLevel.Warning, category, format, args);

        public static void Error(string category, string format, params object[] args) =>
            Log(LogLevel.Error, category, format, args);

        public static void Fatal(string category, string format, params object[] args) =>
            Log(LogLevel.Fatal, category, format, args);

        public static void Debug(ILogContext context, string format, params object[] args) =>
            Log(LogLevel.Debug, context, format, args);

        public static void Info(ILogContext context, string format, params object[] args) =>
            Log(LogLevel.Info, context, format, args);

        public static void Warn(ILogContext context, string format, params object[] args) =>
            Log(LogLevel.Warning, context, format, args);

        public static void Error(ILogContext context, string format, params object[] args) =>
            Log(LogLevel.Error, context, format, args);

        public static void Fatal(ILogContext context, string format, params object[] args) =>
            Log(LogLevel.Fatal, context, format, args);

        public static void Debug(string format, params object[] args) => LogDefault(LogLevel.Debug, format, args);
        public static void Info(string format, params object[] args) => LogDefault(LogLevel.Info, format, args);
        public static void Warn(string format, params object[] args) => LogDefault(LogLevel.Warning, format, args);
        public static void Error(string format, params object[] args) => LogDefault(LogLevel.Error, format, args);
        public static void Fatal(string format, params object[] args) => LogDefault(LogLevel.Fatal, format, args);

        public static void Error(string category, Exception ex)
        {
            var logger = GetLogger(category);
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => logger.Error(prefix + ex.Message + "\n" + ex.StackTrace));
        }

        public static void Error(ILogContext context, Exception ex)
        {
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => context.Logger.Error(prefix + ex.Message + "\n" + ex.StackTrace));
        }

        public static void Error(Exception ex)
        {
            if (string.IsNullOrWhiteSpace(_defaultCategory))
                throw new InvalidOperationException("Default log category is not set.");
            Error(_defaultCategory, ex);
        }
    }
}
