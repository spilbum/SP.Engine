using System;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Fiber;

namespace SP.Core
{
    public static class FiberSchedulerExtensions
    {
        public static void ScheduleAsync(this IFiberScheduler scheduler,
            Func<CancellationToken, Task> work,
            TimeSpan due,
            TimeSpan period,
            CancellationToken shutdown)
        {
            scheduler.Schedule(() =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await work(shutdown).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                    {
                        /* ignore */
                    }
                    catch (Exception ex)
                    {
                        scheduler.Logger.Error(ex, "async job failed");
                    }
                }, shutdown);
            }, due, period);
        }
    }
}
