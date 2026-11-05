using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SP.Core.Logging;

namespace SP.Engine.Server;

public static class AsyncExtensions
{
    private static readonly ILogger Logger = LogManager.GetLogger(nameof(AsyncExtensions));

    public static Task AsyncRun(Action task, Action<Exception> onError = null)
    {
        return Task.Run(task).ContinueWith(t => { HandleException(t, onError); },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public static Task AsyncRun<T>(
        T state,
        Action<T> task,
        Action<Exception> onError = null)
    {
        return Task.Run(() => task(state)).ContinueWith(t =>
        {
            HandleException(t, onError);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void HandleException(Task task, Action<Exception> exceptionHandler)
    {
        if (task.Exception == null)
            return;

        var exceptions = task.Exception.Flatten().InnerExceptions;
        if (exceptionHandler != null)
            foreach (var ex in exceptions)
                exceptionHandler(ex);
        else
            foreach (var ex in exceptions)
                Logger.Error(ex);
    }
}
