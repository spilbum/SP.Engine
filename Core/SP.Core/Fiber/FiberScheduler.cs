using System;
using System.Threading;
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

        public bool TryEnqueue(IAsyncJob job)
            => !_disposed && _fiber.TryEnqueue(job);

        public bool TryEnqueue(Action action)
            => !_disposed && _fiber.TryEnqueue(action);

        public bool TryEnqueue<T>(Action<T> action, T state)
            => !_disposed && _fiber.TryEnqueue(action, state);

        public bool TryEnqueue<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
            => !_disposed && _fiber.TryEnqueue(action, state1, state2);
        
        public bool TryEnqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 state1, T2 state2, T3 state3)
            => !_disposed && _fiber.TryEnqueue(action, state1, state2, state3);
        
        public bool TryEnqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 state1, T2 state2, T3 state3, T4 state4)
            => !_disposed && _fiber.TryEnqueue(action, state1, state2, state3, state4);

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
