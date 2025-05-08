using System;

namespace SP.Common.Fiber
{

    internal interface IAsyncJob
    {
        void Execute(Action<Exception> exceptionHandler);
    }    
}

