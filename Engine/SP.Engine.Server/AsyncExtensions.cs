using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Fiber;
using SP.Core.Logging;

namespace SP.Engine.Server;

public static class AsyncExtensions
{
    public static Task AsyncRun(this ILogContext logContext, Action task, Action<Exception> onError = null)
    {
        return Task.Run(task).ContinueWith(t => { HandleException(logContext, t, onError); },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public static Task AsyncRun(this ILogContext logContext,
        Action<object> task,
        object state,
        Action<Exception> onError = null)
    {
        return Task.Run(() => task(state)).ContinueWith(t => { HandleException(logContext, t, onError); },
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
    
    public static void RunAsync(this IFiber fiber, Func<CancellationToken, Task> task, Action callback = null, 
        Action<Exception> onError = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fiber);

        Task.Run(async () =>
        {
            try
            {
                await task(ct);

                if (callback != null)
                {
                    fiber.Enqueue(callback);
                }
            }
            catch (OperationCanceledException)
            {
                // cancel
            }
            catch (Exception ex)
            {
                if (onError != null)
                {
                    fiber.Enqueue(onError, ex);
                }
                else
                {
                    Console.WriteLine($"[{fiber.Name}] Async task failed: {ex.Message}");
                }
            }
        }, ct);
    }
}
