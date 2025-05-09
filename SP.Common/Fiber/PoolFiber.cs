using System;
using System.Threading.Tasks;
using SP.Common.Logging;

namespace SP.Common.Fiber
{
    public class PoolFiber : IFiberScheduler, IExecutionAction, IDisposable
    {
        private readonly Scheduler _scheduler;
        private readonly ILogger _logger;
        private volatile bool _disposed;

        public PoolFiber(ILogger logger = null)
        {
            _logger = logger;
            _scheduler = new Scheduler(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scheduler.Dispose();
        }

        void IExecutionAction.Enqueue(IAsyncJob job)
        {
            if (_disposed) return;

            _ = Task.Run(() =>
            {
                try
                {
                    job.Execute(_logger);
                }
                catch (Exception e)
                {
                    _logger?.Error(e);
                }
            });
        }

        private void Enqueue(IAsyncJob job)
        {
            ((IExecutionAction)this).Enqueue(job);
        }

        public void Enqueue(Action action)
            => Enqueue(new AsyncJob(action.Target, action.Method));

        public void Enqueue<T>(Action<T> action, T param)
            => Enqueue(new AsyncJob(action.Target, action.Method, param));

        public void Enqueue<T1, T2>(Action<T1, T2> action, T1 p1, T2 p2)
            => Enqueue(new AsyncJob(action.Target, action.Method, p1, p2));

        public void Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 p1, T2 p2, T3 p3)
            => Enqueue(new AsyncJob(action.Target, action.Method, p1, p2, p3));

        public void Enqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 p1, T2 p2, T3 p3, T4 p4)
            => Enqueue(new AsyncJob(action.Target, action.Method, p1, p2, p3, p4));

        public void Enqueue<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            => Enqueue(new AsyncJob(action.Target, action.Method, p1, p2, p3, p4, p5));

        public IDisposable Schedule(Action action, int dueMs, int intervalMs)
            => _scheduler.Schedule(new AsyncJob(action.Target, action.Method), dueMs, intervalMs);

        public IDisposable Schedule<T>(Action<T> action, T param, int dueMs, int intervalMs)
            => _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param), dueMs, intervalMs);

        public IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 p1, T2 p2, int dueMs, int intervalMs)
            => _scheduler.Schedule(new AsyncJob(action.Target, action.Method, p1, p2), dueMs, intervalMs);

        public IDisposable Schedule<T1, T2, T3>(Action<T1, T2, T3> action, T1 p1, T2 p2, T3 p3, int dueMs,
            int intervalMs)
            => _scheduler.Schedule(new AsyncJob(action.Target, action.Method, p1, p2, p3), dueMs, intervalMs);

        public IDisposable Schedule<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 p1, T2 p2, T3 p3, T4 p4,
            int dueMs, int intervalMs)
            => _scheduler.Schedule(new AsyncJob(action.Target, action.Method, p1, p2, p3, p4), dueMs, intervalMs);

        public IDisposable Schedule<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 p1, T2 p2, T3 p3, T4 p4,
            T5 p5, int dueMs, int intervalMs)
            => _scheduler.Schedule(new AsyncJob(action.Target, action.Method, p1, p2, p3, p4, p5), dueMs, intervalMs);
    }
}
