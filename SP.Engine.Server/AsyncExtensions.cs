using System;
using System.Threading.Tasks;
using SP.Engine.Common.Logging;

namespace SP.Engine.Server
{
    public static class AsyncExtensions
    {
        public static Task AsyncRun(this ILoggerProvider logProvider, Action task, Action<Exception> exceptionHandler = null)
        {
            return Task.Run(task).ContinueWith(t => { HandleException(logProvider, t, exceptionHandler); },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public static Task AsyncRun(this ILoggerProvider logProvider, 
            Action<object> task, 
            object state,
            Action<Exception> exceptionHandler = null)
        {
            return Task.Run(() => task(state)).ContinueWith(t => { HandleException(logProvider, t, exceptionHandler); },
                TaskContinuationOptions.OnlyOnFaulted);
        }
    
        private static void HandleException(ILoggerProvider logProvider, Task task, Action<Exception> exceptionHandler)
        {
            if (task.Exception == null)
                return;

            var exceptions = task.Exception.Flatten().InnerExceptions;
            if (exceptionHandler != null)
            {
                foreach (var ex in exceptions)
                    exceptionHandler(ex);
            }
            else
            {
                foreach (var ex in exceptions)
                    logProvider.Logger.WriteLog(ex);
            }
        }
    }
    
}
