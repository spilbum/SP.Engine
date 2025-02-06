using System;

namespace SP.Engine.Common.Fiber
{

    internal interface IAsyncJob
    {
        void Execute(Action<Exception> exceptionHandler);
    }    
}

