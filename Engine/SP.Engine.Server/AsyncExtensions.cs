using System;
using System.Threading.Tasks;
using SP.Core.Logging;

namespace SP.Engine.Server;

public static class AsyncExtensions
{
    public static Task AsyncRun(this ILogContext logContext, Action task, Action<Exception> exceptionHandler = null)
    {
        return Task.Run(task).ContinueWith(t => { HandleException(logContext, t, exceptionHandler); },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public static Task AsyncRun(this ILogContext logContext,
        Action<object> task,
        object state,
        Action<Exception> exceptionHandler = null)
    {
        return Task.Run(() => task(state)).ContinueWith(t => { HandleException(logContext, t, exceptionHandler); },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void HandleException(ILogContext logContext, Task task, Action<Exception> exceptionHandler)
    {
        if (task.Exception == null)
            return;

        var exceptions = task.Exception.Flatten().InnerExceptions;
        if (exceptionHandler != null)
            foreach (var ex in exceptions)
                exceptionHandler(ex);
        else
            foreach (var ex in exceptions)
                logContext.Logger.Error(ex);
    }
}
