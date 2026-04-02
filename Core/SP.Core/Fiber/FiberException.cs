using System;

namespace SP.Core.Fiber
{
    public sealed class FiberException : Exception
    {
        public IFiber Fiber { get; }
        public IWorkJob Job { get; }

        public FiberException(IFiber fiber, IWorkJob job, string message, Exception innerException = null)
            : base($"[{fiber.Name}] Job '{job?.Name}' failed: {message}", innerException)
        {
            Fiber = fiber;
            Job = job;
        }
    }
}
