using System;
using System.Collections.Concurrent;

namespace SP.Core.Fiber
{
    public sealed class Scheduler : IScheduler
    {
        private readonly ConcurrentDictionary<TimerHandleBase, byte> _timers =
            new ConcurrentDictionary<TimerHandleBase, byte>();
        private volatile bool _disposed;

        public IDisposable Schedule(IFiber fiber, Action action, TimeSpan dueTime, TimeSpan period)
        {
            var handle = new TimerHandle(fiber, action);
            return Track(handle, dueTime, period);
        }

        public IDisposable Schedule<T>(IFiber fiber, Action<T> action, T state, TimeSpan dueTime, TimeSpan period)
        {
            var handle = new TimerHandle<T>(fiber, action, state);
            return Track(handle, dueTime, period);
        }

        public IDisposable Schedule<T1, T2>(IFiber fiber, Action<T1, T2> action, T1 s1, T2 s2,
            TimeSpan dueTime, TimeSpan period)
        {
            var handle = new TimerHandle<T1, T2>(fiber, action, s1, s2);
            return Track(handle, dueTime, period);
        }

        public IDisposable Schedule<T1, T2, T3>(IFiber fiber, Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3,
            TimeSpan dueTime, TimeSpan period)
        {
            var handle = new TimerHandle<T1, T2, T3>(fiber, action, s1, s2, s3);
            return Track(handle, dueTime, period);
        }

        private IDisposable Track(TimerHandleBase handle, TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                handle.Dispose();
                return null;
            }

            handle.OnDisposed = h => _timers.TryRemove(h, out _);
            
            _timers.TryAdd(handle, 0);
            handle.Start(dueTime, period);
            return handle;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var timer in _timers.Keys) timer.Dispose();
            _timers.Clear();
        }
    }
}
