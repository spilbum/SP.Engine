using System;

namespace SP.Core.Fiber
{
    public interface IScheduler
    {
        IDisposable Schedule(IFiber fiber, Action action, TimeSpan due, TimeSpan interval);
        IDisposable Schedule<T>(IFiber fiber, Action<T> action, T state, TimeSpan dueTime, TimeSpan period);

        IDisposable Schedule<T1, T2>(IFiber fiber, Action<T1, T2> action, T1 state1, T2 state2, TimeSpan dueTime,
            TimeSpan interval);
    }
}
