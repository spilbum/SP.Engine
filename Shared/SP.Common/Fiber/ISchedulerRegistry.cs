using System;

namespace SP.Common.Fiber
{

    internal interface ISchedulerRegistry
    {
        void Enqueue(IAsyncJob job);
        void Remove(IDisposable job);
    }    
}

