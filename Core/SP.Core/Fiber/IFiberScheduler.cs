using System;
using SP.Core.Logging;

namespace SP.Core.Fiber
{
    public interface IFiberScheduler : ILogContext, IDisposable
    {
        bool IsRunning { get; }
        
        bool TryEnqueue(IAsyncJob job);
        bool TryEnqueue(Action action);
        bool TryEnqueue<T>(Action<T> action, T state);
        bool TryEnqueue<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2);

        IDisposable Schedule(Action action, TimeSpan dueTime, TimeSpan period);
        IDisposable Schedule<T>(Action<T> action, T state, TimeSpan dueTime, TimeSpan period);
        IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2, TimeSpan dueTime, TimeSpan period);
    }
}
