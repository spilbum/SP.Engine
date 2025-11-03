using System;
using System.Threading;
using SP.Core.Logging;

namespace SP.Core.Fiber
{
    public class PoolFiber : IFiber, IDisposable
    {
        private readonly ILogger _logger;
        private volatile bool _disposed;
        private volatile bool _running = true;

        public PoolFiber(ILogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
        }

        public bool TryEnqueue(Action action)
        {
            return TryEnqueue(AsyncJob.From(action));
        }

        public bool TryEnqueue<T>(Action<T> action, T state)
        {
            return TryEnqueue(AsyncJob.From(action, state));
        }

        public bool TryEnqueue<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
        {
            return TryEnqueue(AsyncJob.From(action, state1, state2));
        }

        public bool TryEnqueue(IAsyncJob job)
        {
            if (!_running || _disposed) return false;
            return ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    job.Invoke();
                }
                catch (Exception e)
                {
                    _logger?.Error(e);
                }
            });
        }
    }
}
