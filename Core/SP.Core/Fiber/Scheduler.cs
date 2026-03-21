using System;
using System.Collections.Generic;

namespace SP.Core.Fiber
{
    public sealed class Scheduler : IScheduler
    {
        private readonly HashSet<TimerHandleBase> _timers = new HashSet<TimerHandleBase>();
        private volatile bool _disposed;

        public IDisposable Schedule(IFiber fiber, Action action, TimeSpan due, TimeSpan period)
        {
            ThrowIfDisposed();
            
            var handle = new TimerHandle(fiber, action);
            return Track(handle, due, period);
        }

        public IDisposable Schedule<T>(IFiber fiber, Action<T> action, T state, TimeSpan due, TimeSpan period)
        {
            ThrowIfDisposed();
            
            var handle = new TimerHandle<T>(fiber, action, state);
            return Track(handle, due, period);
        }

        public IDisposable Schedule<T1, T2>(IFiber fiber, Action<T1, T2> action, T1 s1, T2 s2,
            TimeSpan due, TimeSpan period)
        {
            ThrowIfDisposed();
            
            var handle = new TimerHandle<T1, T2>(fiber, action, s1, s2);
            return Track(handle, due, period);
        }

        public IDisposable Schedule<T1, T2, T3>(IFiber fiber, Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3,
            TimeSpan due, TimeSpan period)
        {
            ThrowIfDisposed();

            var handle = new TimerHandle<T1, T2, T3>(fiber, action, s1, s2, s3);
            return Track(handle, due, period);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Scheduler));
        }

        private IDisposable Track(TimerHandleBase handle, TimeSpan dueTime, TimeSpan period)
        {
            handle.OnDisposed = h =>
            {
                lock (_timers)
                {
                    _timers.Remove(h);
                }
            };
            
            lock (_timers)
            {
                if (_disposed)
                {
                    handle.Dispose();
                    throw new ObjectDisposedException(nameof(Scheduler));
                }
                    
                _timers.Add(handle);
                handle.Start(dueTime, period);
            }

            return handle;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            List<TimerHandleBase> temp;
            lock (_timers)
            {
                temp = new List<TimerHandleBase>(_timers);
                _timers.Clear();
            }

            foreach (var t in temp) t.Dispose();
        }
    }
}
