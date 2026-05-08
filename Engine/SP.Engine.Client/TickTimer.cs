using System;
using System.Diagnostics;
using System.Threading;

namespace SP.Engine.Client
{
    public sealed class TickTimer : IDisposable
    {
        private readonly object _state;
        private Action<object> _callback;
        private long _periodTicks;
        private long _nextExecutionTicks;
        private bool _disposed;
        
        public bool IsRunning { get; private set; }

        public TickTimer(Action<object> callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _state = state;
            Change(dueTime, period);
        }

        public void Change(TimeSpan dueTime, TimeSpan period)
        {
            var nowTimestamp = Stopwatch.GetTimestamp();
            
            _periodTicks = period == Timeout.InfiniteTimeSpan
                ? long.MaxValue
                : (long)(period.TotalSeconds * Stopwatch.Frequency);
            
            _nextExecutionTicks = dueTime == Timeout.InfiniteTimeSpan
                ? long.MaxValue
                : nowTimestamp + (long)(dueTime.TotalSeconds * Stopwatch.Frequency);
            
            IsRunning = true;
        }
        
        public void Tick()
        {
            if (!IsRunning || _disposed) return;

            var nowTicks = Stopwatch.GetTimestamp();
            if (nowTicks >= _nextExecutionTicks)
            {
                Execute(nowTicks);
            }
        }

        private void Execute(long nowTicks)
        {
            _callback?.Invoke(_state);

            if (_periodTicks == long.MaxValue)
            {
                Stop();
            }
            else
            {
                _nextExecutionTicks += _periodTicks;

                if (_nextExecutionTicks < nowTicks)
                    _nextExecutionTicks = nowTicks + _periodTicks;
            }
        }

        public void Stop() => IsRunning = false;
        
        public void Dispose()
        {
            if (_disposed) return;
            IsRunning = false;
            _callback = null;
            _disposed = true;
        }
    }
}
