using System;
using SP.Core.Logging;

namespace SP.Core.Fiber
{
    public interface IFiberScheduler : ILogContext, IDisposable
    {
        bool IsRunning { get; }
        
        bool Enqueue(IAsyncJob job);
        bool Enqueue(Action action);
        bool Enqueue<T>(Action<T> action, T state);
        bool Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2);
        bool Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3);
        bool Enqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 s1, T2 s2, T3 s3, T4 s4);

        IDisposable Schedule(Action action, TimeSpan dueTime, TimeSpan period);
        IDisposable Schedule<T>(Action<T> action, T state, TimeSpan dueTime, TimeSpan period);
        IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2, TimeSpan dueTime, TimeSpan period);
        IDisposable Schedule<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3, TimeSpan dueTime, TimeSpan period);
        IDisposable Schedule<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 s1, T2 s2, T3 s3, T4 s4, TimeSpan dueTime, TimeSpan period);
    }
}
