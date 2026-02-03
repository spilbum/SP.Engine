using System;

namespace SP.Core.Fiber
{
    public interface IScheduler
    {
        IDisposable Schedule(IFiber fiber, Action action, TimeSpan due, TimeSpan interval);
        IDisposable Schedule<T>(IFiber fiber, Action<T> action, T state, TimeSpan dueTime, TimeSpan period);
       
        IDisposable Schedule<T1, T2>(IFiber fiber, Action<T1, T2> action, T1 s1, T2 s2, 
            TimeSpan dueTime, TimeSpan interval);

        IDisposable Schedule<T1, T2, T3>(IFiber fiber, Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3,
            TimeSpan dueTime, TimeSpan interval);
        IDisposable Schedule<T1, T2, T3, T4>(IFiber fiber, Action<T1, T2, T3, T4> action, T1 s1, T2 s2, T3 s3, T4 s4,
            TimeSpan dueTime, TimeSpan interval);
    }
}
