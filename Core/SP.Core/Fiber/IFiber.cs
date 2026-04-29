using System;
namespace SP.Core.Fiber
{
    public interface IFiber : IDisposable
    {
        string Name { get; }
        int QueueCount { get; }
        bool IsDisposed { get; }
        bool Enqueue(Action action);
        bool Enqueue<T>(Action<T> action, T state);
        bool Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2);
        bool Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3);
    }

    public struct FiberSnapshot
    {
        public string Name;
        public bool IsWorking;
        
        public int QueuePendingCount;
        public int QueueCapacity;
        
        public long TotalProcessedCount;
        public long TotalDroppedCount;
        public long TotalExceptionCount;
        
        public long TotalDequeueCount;
        public double AvgBatchCount;
    }
}
