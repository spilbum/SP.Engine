using System;
using System.Collections.Concurrent;

namespace SP.Engine.Common.Fiber
{
    internal class Scheduler : IDisposable, ISchedulerRegistry
    {
        private bool _running;
        private readonly IExecutionAction _executionAction;
        private readonly ConcurrentDictionary<IDisposable, bool> _timers;

        public Scheduler(IExecutionAction executionAction)
        {
            _executionAction = executionAction ?? throw new ArgumentNullException(nameof(executionAction));
            _timers = new ConcurrentDictionary<IDisposable, bool>();
            _running = true;
        }

        public IDisposable Schedule(IAsyncJob job, int dueTimeMs, int intervalMs)
        {
            if (!_running) 
                throw new ObjectDisposedException(nameof(Scheduler));
            
            var timer = new TimerAction(this, job, dueTimeMs, intervalMs);
            AddTimer(timer);
            return timer;
        }

        public void Dispose()
        {
            _running = false;
            
            foreach (var timer in _timers.Keys)
                timer.Dispose();
            _timers.Clear();
        }

        private void AddTimer(TimerAction timer)
        {
            if (!_running)
                throw new ObjectDisposedException(nameof(Scheduler));
            
            if (!_timers.TryAdd(timer, true))
                throw new InvalidOperationException("Could not add timer to the scheduler.");
            
            timer.Schedule();
        }

        private void RemoveTimer(IDisposable timer)
        {
            if (_timers.TryRemove(timer, out _))
                timer.Dispose();
        }

        void ISchedulerRegistry.Enqueue(IAsyncJob job)
        {
            _executionAction.Enqueue(job);
        }

        void ISchedulerRegistry.Remove(IDisposable job)
        {
            RemoveTimer(job);
        }
    }
}
