using System;
using System.Collections.Generic;
using System.Security.Cryptography;

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
            return InternalSchedule(fiber, () => fiber.Enqueue(action), dueTime, period);
        }

        public IDisposable Schedule<T>(IFiber fiber, Action<T> action, T state, TimeSpan dueTime, TimeSpan period)
        {
            return InternalSchedule(fiber, () => fiber.Enqueue(action, state), dueTime, period);
        }

        public IDisposable Schedule<T1, T2>(IFiber fiber, Action<T1, T2> action, T1 s1, T2 s2,
            TimeSpan dueTime, TimeSpan period)
        {
            return InternalSchedule(fiber, () => fiber.Enqueue(action, s1, s2), dueTime, period);
        }

        public IDisposable Schedule<T1, T2, T3>(IFiber fiber, Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3,
            TimeSpan dueTime, TimeSpan period)
        {
            return InternalSchedule(fiber, () => fiber.Enqueue(action, s1, s2, s3), dueTime, period);
        }

        public IDisposable Schedule<T1, T2, T3, T4>(IFiber fiber, Action<T1, T2, T3, T4> action, T1 s1, T2 s2, T3 s3,
            T4 s4, TimeSpan dueTime, TimeSpan period)
        {
            return InternalSchedule(fiber, () => fiber.Enqueue(action, s1, s2, s3, s4), dueTime, period);
        }

        private IDisposable InternalSchedule(IFiber fiber, Action tickEnqueue, TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Scheduler));
            if (fiber == null) throw new ArgumentNullException(nameof(fiber));
            if (tickEnqueue == null) throw new ArgumentNullException(nameof(tickEnqueue));

            var ta = new TimerAction(tickEnqueue, dueTime, period);
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
        }
    }
}
