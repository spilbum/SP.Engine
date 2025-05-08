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
        private static ILoggerFactory _loggerFactory = new ConsoleLoggerFactory();
        private static readonly ConcurrentDictionary<string, ILogger> Loggers = new();
        private static readonly List<ThreadFiber> Fibers = [];
        private static int _fiberIndex;
        private static string _defaultCategory;

        static LogManager()
        {
            for (var i = 0; i < Environment.ProcessorCount; i++)
            {
                var fiber = new ThreadFiber(OnError);
                fiber.Start();
                Fibers.Add(fiber);
            }
        }

        public static void SetLoggerFactory(ILoggerFactory factory)
        {
            _loggerFactory = factory;
            Loggers.Clear();
        }

        public static void SetDefaultCategory(string category)
        {
            _defaultCategory = category;
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

        private static void OnError(Exception ex)
        {
            Console.WriteLine("[LogFiberError] {0}", ex);
        }

        public static void Dispose()
        {
            foreach (var fiber in Fibers)
                fiber.Dispose();

            if (_loggerFactory is IDisposable disposableFactory)
                disposableFactory.Dispose();

            try
            {
                Serilog.Log.CloseAndFlush();
            }
            catch (Exception)
            {
                // Serilog가 없거나 예외 발생 시 무시
            }
        }

        private static void Log(ELogLevel level, string category, string message)
        {
            var logger = GetLogger(category);
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => logger.Log(level, prefix + message));
        }

        private static void Log(ELogLevel level, string category, string format, params object[] args)
        {
            var logger = GetLogger(category);
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => logger.Log(level, prefix + string.Format(format, args)));
        }

        private static void Log(ELogLevel level, ILogContext context, string format, params object[] args)
        {
            var method = new StackFrame(2, false).GetMethod();
            var prefix = method != null ? $"[{method.DeclaringType?.Name}.{method.Name}] " : string.Empty;
            Enqueue(() => context.Logger.Log(level, prefix + string.Format(format, args)));
        }

        private static void LogDefault(ELogLevel level, string format, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(_defaultCategory))
                throw new InvalidOperationException("Default log category is not set.");
            Log(level, _defaultCategory, format, args);
        }

        public static void Debug(string category, string format, params object[] args) =>
            Log(ELogLevel.Debug, category, format, args);

        public static void Info(string category, string format, params object[] args) =>
            Log(ELogLevel.Info, category, format, args);

        public static void Warn(string category, string format, params object[] args) =>
            Log(ELogLevel.Warning, category, format, args);

        public static void Error(string category, string format, params object[] args) =>
            Log(ELogLevel.Error, category, format, args);

        public static void Fatal(string category, string format, params object[] args) =>
            Log(ELogLevel.Fatal, category, format, args);

        public static void Debug(ILogContext context, string format, params object[] args) =>
            Log(ELogLevel.Debug, context, format, args);

        public static void Info(ILogContext context, string format, params object[] args) =>
            Log(ELogLevel.Info, context, format, args);

        public static void Warn(ILogContext context, string format, params object[] args) =>
            Log(ELogLevel.Warning, context, format, args);

        public static void Error(ILogContext context, string format, params object[] args) =>
            Log(ELogLevel.Error, context, format, args);

        public static void Fatal(ILogContext context, string format, params object[] args) =>
            Log(ELogLevel.Fatal, context, format, args);

        public static void Debug(string format, params object[] args) => LogDefault(ELogLevel.Debug, format, args);
        public static void Info(string format, params object[] args) => LogDefault(ELogLevel.Info, format, args);
        public static void Warn(string format, params object[] args) => LogDefault(ELogLevel.Warning, format, args);
        public static void Error(string format, params object[] args) => LogDefault(ELogLevel.Error, format, args);
        public static void Fatal(string format, params object[] args) => LogDefault(ELogLevel.Fatal, format, args);

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
