using System;
using SP.Core.Logging;

namespace SP.Core.Fiber
{
    public class FiberScheduler : IFiberScheduler
    {
        private readonly ThreadFiber _fiber;
        private readonly Scheduler _scheduler;
        private volatile bool _disposed;
        private volatile bool _running;

        public bool IsRunning => _running;
        public ILogger Logger { get; }

        public FiberScheduler(
            ILogger logger,
            string name,
            int maxBatchSize = 1024,
            int queueCapacity = -1)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fiber = new ThreadFiber(logger, name, maxBatchSize, queueCapacity);
            _scheduler = new Scheduler();
            _running = true;
        }

        public bool Enqueue(IAsyncJob job)
        {
            ThrowIfDisposed();
            return _fiber.Enqueue(job);
        }

        public bool Enqueue(Action action)
        {
            ThrowIfDisposed();
            return _fiber.Enqueue(action);
        }

        public bool Enqueue<T>(Action<T> action, T state)
        {
            ThrowIfDisposed();
            return _fiber.Enqueue(action, state);
        }

        public bool Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2)
        {
            ThrowIfDisposed();
            return _fiber.Enqueue(action, s1, s2);
        }

        public bool Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3)
        {
            ThrowIfDisposed();
            return _fiber.Enqueue(action, s1, s2, s3);
        }

        public bool Enqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 s1, T2 s2, T3 s3, T4 s4)
        {
            ThrowIfDisposed();
            return _fiber.Enqueue(action, s1, s2, s3, s4);
        }

        public IDisposable Schedule(Action action, TimeSpan dueTime, TimeSpan period)
        {
            ThrowIfDisposed();
            return _scheduler.Schedule(_fiber, action, dueTime, period);
        }

        public IDisposable Schedule<T>(Action<T> action, T state, TimeSpan dueTime, TimeSpan period)
        {
            ThrowIfDisposed();
            return _scheduler.Schedule(_fiber, action, state, dueTime, period);
        }

        public IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2, TimeSpan dueTime,
            TimeSpan period)
        {
            ThrowIfDisposed();
            return _scheduler.Schedule(_fiber, action, s1, s2, dueTime, period);
        }

        public IDisposable Schedule<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3,
            TimeSpan dueTime, TimeSpan period)
        {
            ThrowIfDisposed();
            return _scheduler.Schedule(_fiber, action, s1, s2, s3, dueTime, period);
        }

        public IDisposable Schedule<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 s1, T2 s2, T3 s3, T4 s4,
            TimeSpan dueTime, TimeSpan period)
        {
            ThrowIfDisposed();
            return _scheduler.Schedule(_fiber, action, s1, s2, s3, s4, dueTime, period);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IFiberScheduler));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            
            try
            {
                _scheduler.Dispose();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Scheduler dispose failed");
            }

            try
            {
                _fiber.Dispose();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Fiber dispose failed");
            }
        }
    }
}
