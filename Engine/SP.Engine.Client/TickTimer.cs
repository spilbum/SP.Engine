using System;
using System.Collections.Generic;
using System.Threading;

namespace SP.Engine.Client
{
    public sealed class TickTimer : IDisposable
    {
        private DateTime _lastExecutionTime;
        private readonly int _dueTimeMs;
        private readonly int _intervalMs;
        private readonly object _state;
        private Action<object> _callback;
        private bool _isFirstExecution;
        
        public bool IsRunning { get; private set; }

        public TickTimer(Action<object> callback, object state, int dueTimeMs, int intervalMs)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _state = state;
            _dueTimeMs = dueTimeMs;
            _intervalMs = intervalMs;
            _lastExecutionTime = DateTime.UtcNow;
            _isFirstExecution = true;
            IsRunning = true;
        }

        public void Tick()
        {
            if (!IsRunning) 
                return;

            var now = DateTime.UtcNow;
            var elapsedMs = (int)(now - _lastExecutionTime).TotalMilliseconds;

            if (_isFirstExecution)
            {
                if (_dueTimeMs == Timeout.Infinite || elapsedMs < _dueTimeMs)
                    return;
                
                Execute(now);
                _isFirstExecution = false;

                if (_intervalMs == Timeout.Infinite)
                    Dispose();
            }
            else
            {
                if (_intervalMs == Timeout.Infinite || elapsedMs < _intervalMs) 
                    return;
                
                Execute(now);
            }
        }

        private void Execute(DateTime now)
        {
            _lastExecutionTime = now;
            _callback?.Invoke(_state);
        }

        public void Dispose()
        {
            IsRunning = false;
            _callback = null;
        }
    }
}
