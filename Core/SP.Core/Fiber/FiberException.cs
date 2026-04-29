using System;

namespace SP.Core.Fiber
{
    [Serializable]
    public sealed class FiberException : Exception
    {
        public string FiberName { get; }
        public IWorkJob Job { get; }

        public FiberException(string fiberName, IWorkJob job, string message, Exception innerException = null)
            : base($"Fiber '{fiberName}' Job '{job?.Name}' failed: {message}", innerException)
        {
            FiberName = fiberName;
            Job = job;
        }
    }

    [Serializable]
    public sealed class FiberQueueFullException : Exception
    {
        public string FiberName { get; }
        public int PendingCount { get; }
        public int Capacity { get; }
        public long DroppedCount { get; }
        public IWorkJob DroppedJob { get; }

        public FiberQueueFullException(string fiberName, int pendingCount, int capacity, long droppedCount, IWorkJob job)
            : base($"Fiber '{fiberName}' queue is full ({pendingCount}/{capacity}). Job '{job?.Name}' dropped.")
        {
            FiberName = fiberName;
            PendingCount = pendingCount;
            Capacity = capacity;
            DroppedCount = droppedCount;
            DroppedJob = job;
        }
    }
}
