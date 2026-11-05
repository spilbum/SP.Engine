using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace SP.Core.Logging
{
    public static class LogManager
    {
        private static readonly ConcurrentDictionary<string, ProxyLogger> Loggers = new ConcurrentDictionary<string, ProxyLogger>();
        private static volatile ILoggerFactory _factory = new ConsoleLoggerFactory(GetDefaultMinimumLevel());
        private static string _defaultCategory = "Default";

        public static void Initialize(ILoggerFactory factory, string defaultCategory = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            if (!string.IsNullOrWhiteSpace(defaultCategory))
            {
                _defaultCategory = defaultCategory;
            }

            var oldFactory = _factory;
            _factory = factory;

            foreach (var kv in Loggers)
            {
                kv.Value.Update(_factory.CreateLogger(kv.Key));
            }

            if (oldFactory != null && !ReferenceEquals(oldFactory, factory))
            {
                try
                {
                    oldFactory.Dispose();
                }
                catch
                {
                    /* ignored */
                }
            }
        }

        public static void InitializeConsole(LogLevel minLevel, string defaultCategory = null)
        {
            Initialize(new ConsoleLoggerFactory(minLevel), defaultCategory);
        }

        public static ILogger GetLogger(string category = null)
        {
            var cat = string.IsNullOrWhiteSpace(category) ? _defaultCategory : category;
            return Loggers.GetOrAdd(cat, key => new ProxyLogger(_factory.CreateLogger(key)));
        }

        public static ILogger GetLogger<T>()
        {
            return GetLogger(typeof(T).Name);
        }

        public static ILogger GetLogger(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return GetLogger(type.Name);
        }

        public static void Dispose()
        {
            var oldFactory = _factory;
            _factory = new ConsoleLoggerFactory(GetDefaultMinimumLevel());

            foreach (var kv in Loggers)
            {
                kv.Value.Update(_factory.CreateLogger(kv.Key));
            }

            try
            {
                oldFactory?.Dispose();
            }
            catch
            {
                /* ignored */
            }
        }

        private static LogLevel GetDefaultMinimumLevel()
        {
            var entry = Assembly.GetEntryAssembly();
            var debugAttr = entry != null ? CustomAttributeExtensions.GetCustomAttribute<DebuggableAttribute>(entry) : null;
            return debugAttr != null && debugAttr.IsJITTrackingEnabled
                ? LogLevel.Debug
                : LogLevel.Information;
        }

        private sealed class ProxyLogger : ILogger
        {
            private volatile ILogger _inner;

            public ProxyLogger(ILogger inner)
            {
                _inner = inner;
            }

            public void Update(ILogger inner)
            {
                _inner = inner;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                _inner?.Log(logLevel, eventId, state, exception, formatter);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                var inner = _inner;
                return inner != null && inner.IsEnabled(logLevel);
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return _inner?.BeginScope(state);
            }
        }

        private sealed class ConsoleLoggerFactory : ILoggerFactory
        {
            private readonly LogLevel _minLevel;

            public ConsoleLoggerFactory(LogLevel minLevel)
            {
                _minLevel = minLevel;
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new ConsoleLogger(categoryName, _minLevel);
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
