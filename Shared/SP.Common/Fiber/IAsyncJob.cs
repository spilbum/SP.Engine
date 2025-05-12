using SP.Common.Logging;

namespace SP.Common.Fiber
{
    internal interface IAsyncJob
    {
        void Execute(ILogger logger);
    }    
}

