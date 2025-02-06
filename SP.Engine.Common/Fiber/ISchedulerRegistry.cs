using System;

namespace SP.Engine.Common.Fiber
{

    internal interface ISchedulerRegistry
    {
        void Enqueue(IAsyncJob job);
        void Remove(IDisposable job);
    }    
}

