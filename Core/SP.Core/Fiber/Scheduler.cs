using System;
using System.Collections.Generic;

namespace SP.Core.Fiber
{
    public class Scheduler : IScheduler, IDisposable
    {
        private readonly List<TimerAction> _timers = new List<TimerAction>();
        private volatile bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            List<TimerAction> temp;
            lock (_timers)
            {
                temp = new List<TimerAction>(_timers);
                _timers.Clear();
            }

            foreach (var t in temp) t.Disable();
            foreach (var t in temp) t.Dispose();
        }

        public IDisposable Schedule(IFiber fiber, Action action, TimeSpan dueTime, TimeSpan period)
        {
            return Schedule(fiber, AsyncJob.From(action), dueTime, period);
        }

        public IDisposable Schedule<T>(IFiber fiber, Action<T> action, T state, TimeSpan dueTime, TimeSpan period)
        {
            return Schedule(fiber, AsyncJob.From(action, state), dueTime, period);
        }

        public IDisposable Schedule<T1, T2>(IFiber fiber, Action<T1, T2> action, T1 state1, T2 state2, TimeSpan dueTime,
            TimeSpan period)
        {
            return Schedule(fiber, AsyncJob.From(action, state1, state2), dueTime, period);
        }

        public IDisposable Schedule(IFiber fiber, IAsyncJob job, TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Scheduler));
            if (fiber == null) throw new ArgumentNullException(nameof(fiber));
            if (job == null) throw new ArgumentNullException(nameof(job));

            var ta = new TimerAction(EnqueueOnce, dueTime, period);
            lock (_timers)
            {
                if (_disposed)
                {
                    ta.Dispose();
                    throw new ObjectDisposedException(nameof(Scheduler));
                }

                _timers.Add(ta);
            }

            return ta;

            void EnqueueOnce()
            {
                fiber.TryEnqueue(job);
            }
        }
    }
}
