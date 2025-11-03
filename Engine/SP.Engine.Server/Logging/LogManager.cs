using System;
using System.Collections.Concurrent;
using Serilog;
using SP.Core.Logging;
using ILogger = SP.Core.Logging.ILogger;

namespace SP.Engine.Server.Logging;

public static class LogManager
{
    private static readonly ConcurrentDictionary<string, ILogger> Cached = new();
    private static ILoggerFactory _factory;
    private static string _defaultCategory;
    private static volatile bool _initialized;

    public static void Initialize(string defaultCategory, ILoggerFactory factory)
    {
        _defaultCategory = defaultCategory;
        _factory = factory;
        _initialized = true;
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        throw new InvalidOperationException("LogManager is not initialized.");
    }

    public static ILogger GetLogger(string category = null)
    {
        EnsureInitialized();
        var cat = string.IsNullOrWhiteSpace(category) ? _defaultCategory : category;
        return Cached.GetOrAdd(cat, key => _factory.GetLogger(key));
    }

    public static ILogger GetLogger<T>()
    {
        return GetLogger(typeof(T).FullName);
    }


    public static void Dispose()
    {
        try
        {
            Log.CloseAndFlush();
        }
        catch
        {
            /* ignored */
        }

        Cached.Clear();
        _initialized = false;
    }

    public static void Debug(string category, string message)
    {
        GetLogger(category).Debug(message);
    }

    public static void Debug(string category, string format, params object[] args)
    {
        GetLogger(category).Debug(format, args);
    }

    public static void Info(string category, string message)
    {
        GetLogger(category).Info(message);
    }

    public static void Info(string category, string format, params object[] args)
    {
        GetLogger(category).Info(format, args);
    }

    public static void Warn(string category, string message)
    {
        GetLogger(category).Warn(message);
    }

    public static void Warn(string category, string format, params object[] args)
    {
        GetLogger(category).Warn(format, args);
    }

    public static void Error(string category, string message)
    {
        GetLogger(category).Error(message);
    }

    public static void Error(string category, string format, params object[] args)
    {
        GetLogger(category).Error(format, args);
    }

    public static void Error(string category, Exception ex)
    {
        GetLogger(category).Error(ex);
    }

    public static void Error(string category, Exception ex, string format, params object[] args)
    {
        GetLogger(category).Error(ex, format, args);
    }

    public static void Fatal(string category, string message)
    {
        GetLogger(category).Fatal(message);
    }

    public static void Fatal(string category, string format, params object[] args)
    {
        GetLogger(category).Fatal(format, args);
    }

    public static void Fatal(string category, Exception ex)
    {
        GetLogger(category).Fatal(ex);
    }

    public static void Fatal(string category, Exception ex, string format, params object[] args)
    {
        GetLogger(category).Fatal(ex, format, args);
    }

    public static void Debug(ILogContext ctx, string message)
    {
        (ctx ?? throw new ArgumentNullException(nameof(ctx))).Logger.Debug(message);
    }

    public static void Debug(ILogContext ctx, string format, params object[] args)
    {
        ctx.Logger.Debug(format, args);
    }

    public static void Info(ILogContext ctx, string message)
    {
        ctx.Logger.Info(message);
    }

    public static void Info(ILogContext ctx, string format, params object[] args)
    {
        ctx.Logger.Info(format, args);
    }

    public static void Warn(ILogContext ctx, string message)
    {
        ctx.Logger.Warn(message);
    }

    public static void Warn(ILogContext ctx, string format, params object[] args)
    {
        ctx.Logger.Warn(format, args);
    }

    public static void Error(ILogContext ctx, string message)
    {
        ctx.Logger.Error(message);
    }

    public static void Error(ILogContext ctx, string format, params object[] args)
    {
        ctx.Logger.Error(format, args);
    }

    public static void Error(ILogContext ctx, Exception ex)
    {
        ctx.Logger.Error(ex);
    }

    public static void Error(ILogContext ctx, Exception ex, string format, params object[] args)
    {
        ctx.Logger.Error(ex, format, args);
    }

    public static void Fatal(ILogContext ctx, string message)
    {
        ctx.Logger.Fatal(message);
    }

    public static void Fatal(ILogContext ctx, string format, params object[] args)
    {
        ctx.Logger.Fatal(format, args);
    }

    public static void Fatal(ILogContext ctx, Exception ex)
    {
        ctx.Logger.Fatal(ex);
    }

    public static void Fatal(ILogContext ctx, Exception ex, string format, params object[] args)
    {
        ctx.Logger.Fatal(ex, format, args);
    }

    public static void Debug(string message)
    {
        GetLogger().Debug(message);
    }

    public static void Debug(string format, params object[] args)
    {
        GetLogger().Debug(format, args);
    }

    public static void Info(string message)
    {
        GetLogger().Info(message);
    }

    public static void Info(string format, params object[] args)
    {
        GetLogger().Info(format, args);
    }

    public static void Warn(string message)
    {
        GetLogger().Warn(message);
    }

    public static void Warn(string format, params object[] args)
    {
        GetLogger().Warn(format, args);
    }

    public static void Error(string message)
    {
        GetLogger().Error(message);
    }

    public static void Error(string format, params object[] args)
    {
        GetLogger().Error(format, args);
    }

    public static void Error(Exception ex)
    {
        GetLogger().Error(ex);
    }

    public static void Error(Exception ex, string format, params object[] args)
    {
        GetLogger().Error(ex, format, args);
    }

    public static void Fatal(string message)
    {
        GetLogger().Fatal(message);
    }

    public static void Fatal(string format, params object[] args)
    {
        GetLogger().Fatal(format, args);
    }

    public static void Fatal(Exception ex)
    {
        GetLogger().Fatal(ex);
    }

    public static void Fatal(Exception ex, string format, params object[] args)
    {
        GetLogger().Fatal(ex, format, args);
    }
}
