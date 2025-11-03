using System;
using SP.Core.Logging;

namespace SP.Core.Fiber
{
    public class FiberScheduler : IFiberScheduler
    {
        private readonly ThreadFiber _fiber;
        private readonly ILogger _logger;
        private readonly Scheduler _scheduler;
        private volatile bool _disposed;

        public FiberScheduler(
            ILogger logger,
            string name,
            int maxBatchSize = 1024,
            int queueCapacity = -1)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fiber = new ThreadFiber(logger, name, maxBatchSize, queueCapacity);
            _scheduler = new Scheduler();
        }

        public bool TryEnqueue(IAsyncJob job)
        {
            return !_disposed && _fiber.TryEnqueue(job);
        }

        public bool TryEnqueue(Action action)
        {
            return !_disposed && _fiber.TryEnqueue(action);
        }

        public bool TryEnqueue<T>(Action<T> action, T state)
        {
            return !_disposed && _fiber.TryEnqueue(action, state);
        }

        public bool TryEnqueue<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
        {
            return !_disposed && _fiber.TryEnqueue(action, state1, state2);
        }

        public IDisposable Schedule(Action action, TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IFiberScheduler));
            return _scheduler.Schedule(_fiber, action, dueTime, period);
        }

        public IDisposable Schedule<T>(Action<T> action, T state, TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IFiberScheduler));
            return _scheduler.Schedule(_fiber, action, state, dueTime, period);
        }

        public IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2, TimeSpan dueTime,
            TimeSpan period)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IFiberScheduler));
            return _scheduler.Schedule(_fiber, action, state1, state2, dueTime, period);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _scheduler.Dispose();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Scheduler dispose failed");
            }

            try
            {
                _fiber.Dispose();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Fiber dispose failed");
            }
        }
    }
}
