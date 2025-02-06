using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SP.Engine.Common.Logging;
using SP.Engine.Common.Fiber;

namespace SP.Engine.Server.Logging
{
    public static class LogManager
    {
        private class LogJob
        {
            public readonly string Name;
            public readonly ELogLevel Level;
            public readonly string Format;
            public readonly object[] Args;

            public LogJob(string name, ELogLevel level, string format, object[] args)
            {
                Name = name;
                Level = level;
                Format = format;
                Args = args;
            }
        }
        
        private const string DefaultLoggerName = "DefaultLogger";
        private static readonly ConcurrentDictionary<string, ILogger> LoggerDict = new ConcurrentDictionary<string, ILogger>();
        private static readonly ConcurrentQueue<LogJob> LogJobQueue = new ConcurrentQueue<LogJob>();
        private static readonly List<ThreadFiber> Fibers = new List<ThreadFiber>();
        private static int _fiberIndex;
        private static ILoggerFactory _loggerFactory;

        private static int GetIndex()
        {
            if (_fiberIndex >= int.MaxValue)
                _fiberIndex = 0;
            else            
                Interlocked.Increment(ref _fiberIndex);
            return _fiberIndex;
        }
        
        public static bool Initialize(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory
                ?? new ConsoleLoggerFactory();

            var fiberCnt = Math.Min(Environment.ProcessorCount * 2, 8);
            for (var index = 0; index < fiberCnt; index++)
                AddFiber();

            return true;
        }

        public static void Dispose()
        {
            foreach (var fiber in Fibers)
                fiber.Dispose();
        }
        
        private static void AddFiber()
        {
            var fiber = new ThreadFiber(OnError);
            fiber.Start();
            Fibers.Add(fiber);
        }

        public static ILogger GetLogger(string name)
        {
            return LoggerDict.GetOrAdd(name ?? DefaultLoggerName, n => _loggerFactory.GetLogger(n));
        }

        public static void WriteLog(string name, ELogLevel level, string format, params object[] args)
        {
            var stackFrame = new StackFrame(1, false);
            var method = stackFrame.GetMethod();
            format = null == method ? format : $"[{method.DeclaringType?.Name}.{method.Name}] {format}";
            
            var logJob = new LogJob(name, level, format, args);
            EnqueueLogJob(logJob);
        }

        public static void WriteLog(ELogLevel level, string format, params object[] args)
        {
            var stackFrame = new StackFrame(1, false);
            var method = stackFrame.GetMethod();
            format = null == method ? format : $"[{method.DeclaringType?.Name}.{method.Name}] {format}";
            
            var logJob = new LogJob(DefaultLoggerName, level, format, args);
            EnqueueLogJob(logJob);
        }
        
        private static void EnqueueLogJob(LogJob job)
        {
            var fiberIndex = GetIndex();
            var fiber = Fibers[fiberIndex % Fibers.Count];
            fiber.Enqueue(ProcessLogJob, job);
        }

        private static void ProcessLogJob(LogJob job)
        {
            try
            {
                var logger = GetLogger(job.Name);
                logger.WriteLog(job.Level, job.Format, job.Args);
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private static void OnError(Exception ex)
        {
            var logger = GetLogger(DefaultLoggerName);
            logger.WriteLog(ELogLevel.Error, "Logging failed: {0}\r\n{1}", ex.Message, ex.StackTrace);
        }
    }
}
