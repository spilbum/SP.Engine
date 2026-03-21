using System;
namespace SP.Core.Fiber
{
    public interface IFiber : IDisposable
    {
        string Name { get; }
        bool Enqueue(Action action);
        bool Enqueue<T>(Action<T> action, T state);
        bool Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2);
        bool Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3);
    }
}
