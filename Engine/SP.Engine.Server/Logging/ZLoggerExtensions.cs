using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SP.Engine.Server.Logging;

public static class ZLoggerExtensions
{
    public static void Debug(
        this ILogger logger,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerDebugInterpolatedStringHandler message)
    {
        logger.ZLogDebug(ref message);
    }

    public static void Debug(
        this ILogger logger,
        Exception exception,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerDebugInterpolatedStringHandler message)
    {
        logger.ZLogDebug(exception, ref message);
    }

    public static void Info(
        this ILogger logger,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerInformationInterpolatedStringHandler message)
    {
        logger.ZLogInformation(ref message);
    }

    public static void Info(
        this ILogger logger,
        Exception exception,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerInformationInterpolatedStringHandler message)
    {
        logger.ZLogInformation(exception, ref message);
    }

    public static void Warn(
        this ILogger logger,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerWarningInterpolatedStringHandler message)
    {
        logger.ZLogWarning(ref message);
    }

    public static void Warn(
        this ILogger logger,
        Exception exception,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerWarningInterpolatedStringHandler message)
    {
        logger.ZLogWarning(exception, ref message);
    }

    public static void Error(
        this ILogger logger,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerErrorInterpolatedStringHandler message)
    {
        logger.ZLogError(ref message);
    }

    public static void Error(
        this ILogger logger,
        Exception exception,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerErrorInterpolatedStringHandler message)
    {
        logger.ZLogError(exception, ref message);
    }

    public static void Fatal(
        this ILogger logger,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerCriticalInterpolatedStringHandler message)
    {
        logger.ZLogCritical(ref message);
    }

    public static void Fatal(
        this ILogger logger,
        Exception exception,
        [InterpolatedStringHandlerArgument(nameof(logger))] ref ZLoggerCriticalInterpolatedStringHandler message)
    {
        logger.ZLogCritical(exception, ref message);
    }
}
