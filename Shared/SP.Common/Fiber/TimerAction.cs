﻿
using System;
using System.Threading;

namespace SP.Common.Fiber
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
        private int _executing = 0;

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
            if (Interlocked.Exchange(ref _executing, 1) == 1)
                return;

            try
            {
                lock (_lock)
                {
                    if (!_running || _disposed) 
                        return;
                }     
                
                _registry.Enqueue(_job);
            }
            finally
            {
                Interlocked.Exchange(ref _executing, 0);
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
