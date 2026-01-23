using System;
using System.Threading;

namespace SP.Core.Fiber
{
    internal sealed class TimerAction : IDisposable
    {
        private readonly Action _tickEnqueue;
        private volatile bool _disposed;
        private volatile bool _enabled = true;
        private int _gate; // 0:idle, 1:running
        private Timer _timer;

        public TimerAction(Action tickEnqueue, TimeSpan dueTime, TimeSpan period)
        {
            _tickEnqueue = tickEnqueue ?? throw new ArgumentNullException(nameof(tickEnqueue));
            if (period <= TimeSpan.Zero)
                period = Timeout.InfiniteTimeSpan;

            _timer = new Timer(Callback, null, dueTime, period);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;

            var t = Interlocked.Exchange(ref _timer, null);
            t?.Dispose();
        }

        private void Callback(object state)
        {
            if (!_enabled || _disposed) return;

            if (Interlocked.Exchange(ref _gate, 1) == 1) return;
            try
            {
                if (!_enabled || _disposed) return;
                _tickEnqueue();
            }
            finally
            {
                Volatile.Write(ref _gate, 0);
            }
        }

        public void Disable()
        {
            _enabled = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }
}
