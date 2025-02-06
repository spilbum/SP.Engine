namespace SP.Engine.Common.Fiber
{

    internal interface IExecutionAction
    {
        void Enqueue(IAsyncJob job);
    }    
}

