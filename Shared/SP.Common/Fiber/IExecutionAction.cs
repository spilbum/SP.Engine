namespace SP.Common.Fiber
{

    internal interface IExecutionAction
    {
        void Enqueue(IAsyncJob job);
    }    
}

