using System;

namespace SP.Core.Fiber
{
    public interface IFiber
    {
        bool TryEnqueue(IAsyncJob job);
        bool TryEnqueue(Action action);
        bool TryEnqueue<T>(Action<T> action, T state);
        bool TryEnqueue<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2);
    }
}
