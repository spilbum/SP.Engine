using System;
using System.Threading;

namespace SP.Core.Fiber
{
    internal abstract class TimerHandleBase : IDisposable
    {
        private Timer _timer;
        private int _gate;
        private volatile bool _disposed;
        internal Action<TimerHandleBase> OnDisposed;

        protected TimerHandleBase()
        {
            _timer = new Timer(Callback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) return;
            _timer?.Change(dueTime, period <= TimeSpan.Zero ? Timeout.InfiniteTimeSpan : period);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var t = Interlocked.Exchange(ref _timer, null);
            t?.Dispose();
            OnDisposed?.Invoke(this);
        }

        private void Callback(object state)
        {
            if (_disposed) return;
            if (Interlocked.CompareExchange(ref _gate, 1, 0) != 0) return;

            try
            {
                if (!_disposed)
                {
                    OnTick();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _gate, 0);
            }
        }
        
        protected abstract void OnTick();
    }
    
    internal sealed class TimerHandle : TimerHandleBase
    {
        private readonly IFiber _fiber;
        private readonly Action _action;

        public TimerHandle(IFiber fiber, Action action)
        {
            _fiber = fiber;
            _action = action;
  
        }

        protected override void OnTick()
        {
            if (_fiber.IsDisposed)
                Dispose();

            _fiber.Enqueue(_action);
        }
    }
    
    internal sealed class TimerHandle<T1> : TimerHandleBase
    {
        private readonly IFiber _fiber;
        private readonly Action<T1> _action;
        private readonly T1 _state;

        public TimerHandle(IFiber fiber, Action<T1> action, T1 state)
        {
            _fiber = fiber;
            _action = action;
            _state = state;;
        }

        protected override void OnTick()
        {
            if (!_fiber.Enqueue(_action, _state))
            {
                Dispose();
            }
        }
    }
    
    internal sealed class TimerHandle<T1, T2> : TimerHandleBase
    {
        private readonly IFiber _fiber;
        private readonly Action<T1, T2> _action;
        private readonly T1 _s1;
        private readonly T2 _s2;

        public TimerHandle(IFiber fiber, Action<T1, T2> action, T1 s1, T2 s2)
        {
            _fiber = fiber;
            _action = action;
            _s1 = s1; 
            _s2 = s2;
        }

        protected override void OnTick()
        {
            if (!_fiber.Enqueue(_action, _s1, _s2))
            {
                Dispose();
            }
        }
    }
    
    internal sealed class TimerHandle<T1, T2, T3> : TimerHandleBase
    {
        private readonly IFiber _fiber;
        private readonly Action<T1, T2, T3> _action;
        private readonly T1 _s1;
        private readonly T2 _s2;
        private readonly T3 _s3;

        public TimerHandle(IFiber fiber, Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3)
        {
            _fiber = fiber;
            _action = action;
            _s1 = s1;
            _s2 = s2; 
            _s3 = s3;
        }

        protected override void OnTick()
        {
            if (!_fiber.Enqueue(_action, _s1, _s2, _s3))
            {
                Dispose();
            }
        }
    }
}
