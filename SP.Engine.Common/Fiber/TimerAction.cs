
using System;
using System.Threading;

namespace SP.Engine.Common.Fiber
{

    internal sealed class TimerAction : IDisposable
    {
        private readonly ISchedulerRegistry _registry;
        private readonly IAsyncJob _job;
        private readonly int _dueTimeMs;
        private readonly int _intervalMs;
        private Timer _timer;
        private volatile bool _running = true;
        private readonly object _lock = new object();
        private bool _disposed;

        public TimerAction(ISchedulerRegistry registry, IAsyncJob job, int dueTimeMs, int intervalMs)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _job = job ?? throw new ArgumentNullException(nameof(job));
            _dueTimeMs = dueTimeMs;
            _intervalMs = intervalMs;
        }

        public void Schedule()
        {
            lock (_lock)
            {
                if (!_running || _disposed) return;
                _timer = new Timer(state => { Execute(); }, null, _dueTimeMs, _intervalMs);
            }            
        }

        private void Execute()
        {
            lock (_lock)
            {
                if (!_running || _disposed) return;
                _registry.Enqueue(_job);
            }            
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _running = false;

                Interlocked.Exchange(ref _timer, null)?.Dispose();
                _registry.Remove(this);
            }            
        }
    }
}
