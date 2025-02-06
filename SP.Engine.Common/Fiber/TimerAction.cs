
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
        private bool _running;

        public TimerAction(ISchedulerRegistry registry, IAsyncJob job, int dueTimeMs, int intervalMs)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _job = job ?? throw new ArgumentNullException(nameof(job));
            _dueTimeMs = dueTimeMs;
            _intervalMs = intervalMs;
            _running = true;
        }

        public void Schedule()
        {
            if (!_running) return;
            _timer = new Timer(state => { Execute(); }, null, _dueTimeMs, _intervalMs);
        }

        private void Execute()
        {
            if (_running)
                _registry.Enqueue(_job);
            else
                Dispose();
        }

        public void Dispose()
        {
            _running = false;
            Interlocked.Exchange(ref _timer, null)?.Dispose();
            _registry.Remove(this);
        }
    }
}
