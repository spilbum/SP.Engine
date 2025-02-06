
using System;
using System.Collections.Generic;

namespace SP.Engine.Common.Logging
{
    public class ConsoleLogger : ILogger
    {
        private readonly string _name;
        private readonly object _lock = new object();
        private const string MessageTemplate = "{0}-{1}: {2}";
        private const string ExceptionFormat = "An exception occurred: {0}{1}stackTrace={2}";
        private const string MessageAndExceptionFormat = "An exception occurred: {0}, exception={1}{2}stackTrace={3}";

        private static readonly Dictionary<ELogLevel, string> LogLevelMap = new Dictionary<ELogLevel, string>()
        {
            { ELogLevel.Debug, "DEBUG" },
            { ELogLevel.Error, "ERROR" },
            { ELogLevel.Fatal, "FATAL" },
            { ELogLevel.Info, "INFO" },
            { ELogLevel.Warning, "WARN" }
        };

        public ConsoleLogger(string name)
        {
            _name = name ?? "none";
        }

        public void WriteLog(ELogLevel logLevel, string format, params object[] args)
        {
            if (string.IsNullOrEmpty(format))
                return;

            if (!LogLevelMap.TryGetValue(logLevel, out var logLevelString))
                throw new ArgumentException($"Invalid log level: {logLevel}");

            var message = args == null ? format : string.Format(format, args);

            lock (_lock)
            {
                Console.WriteLine(MessageTemplate, _name, logLevelString, message);
            }
        }

        public void WriteLog(Exception exception)
        {
            WriteLog(ELogLevel.Error, ExceptionFormat, exception.Message, Environment.NewLine, exception.StackTrace);
        }

        public void WriteLog(string message, Exception ex)
        {
            WriteLog(ELogLevel.Error, MessageAndExceptionFormat, message, ex.Message, Environment.NewLine, ex.StackTrace);
        }
    }

}
