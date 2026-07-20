using System;
using System.Threading;

namespace SP.Core.Fibers
{
    public sealed class PoolFiber : IFiber
    {
        private readonly BatchQueue<IWorkJob> _queue;
        private readonly Action<Exception> _onError;

        private int _isExecuting;
        private volatile bool _disposed;
        private readonly IWorkJob[] _batchBuffer;

        public string Name { get; }
        public bool IsDisposed => _disposed;

        public PoolFiber(string name, int capacity = 4096, int maxBatchSize = 256, Action<Exception> onError = null)
        {
            Name = name;
            _queue = new BatchQueue<IWorkJob>(capacity);
            _batchBuffer = new IWorkJob[maxBatchSize];
            _onError = onError;
        }

        public bool Enqueue(Action action) => Enqueue(WorkJob.From(action));
        public bool Enqueue<T>(Action<T> action, T state) => Enqueue(WorkJob.From(action, state));
        public bool Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2) => Enqueue(WorkJob.From(action, s1, s2));
        public bool Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3) => Enqueue(WorkJob.From(action, s1, s2, s3));

        private bool Enqueue(IWorkJob job)
        {
            if (job == null) return false;
            if (_disposed)
            {
                job.Dispose();
                return false;
            }

            var spinner = new SpinWait();
            while (true)
            {
                var result = _queue.TryEnqueue(job);
                switch (result)
                {
                    case EnqueueResult.Success:
                        TryScheduleExecution();
                        return true;

                    case EnqueueResult.Contention:
                        spinner.SpinOnce();
                        continue;

                    case EnqueueResult.Full:
                        if (spinner.NextSpinWillYield) Thread.Sleep(1);
                        else Thread.Yield();
                        continue;

                    case EnqueueResult.Closed:
                    default:
                        job.Dispose();
                        return false;
                }
            }
        }

        private void TryScheduleExecution()
        {
            if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) == 0)
            {
                ThreadPool.UnsafeQueueUserWorkItem(ExecutePoolLoop, this);
            }
        }

        private static void ExecutePoolLoop(object state)
        {
            var fiber = (PoolFiber)state;
            if (fiber._disposed) return;

            var batchBuf = fiber._batchBuffer;

            try
            {
                while (!fiber._disposed)
                {
                    var count = fiber._queue.DequeueBatch(batchBuf);
                    if (count == 0) break;

                    for (var i = 0; i < count; i++)
                    {
                        var job = batchBuf[i];
                        if (job == null) continue;

                        try
                        {
                            job.Execute();
                        }
                        catch (Exception ex)
                        {
                            fiber._onError?.Invoke(new FiberException(fiber.Name, job, ex.Message, ex));
                        }
                        finally
                        {
                            job.Dispose();
                            batchBuf[i] = null;
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref fiber._isExecuting, 0);

                if (fiber._queue.PendingCount > 0)
                {
                    fiber.TryScheduleExecution();
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.Close();
            _queue.Dispose();
        }
    }
}
