using System;
using System.Net;
using System.Threading;

namespace SP.Core.Fiber
{
    public sealed class ThreadFiber : IFiber
    {
        private readonly BatchQueue<IWorkJob> _queue;
        private readonly Action<Exception> _onError;
        private readonly int _maxBatchSize;
        private readonly Thread _thread;
        private volatile bool _disposed;
        
        public string Name { get; }
        public bool IsDisposed => _disposed;
        public int QueueCount => _queue.PendingCount;

        private long _totalExceptionCount;

        public ThreadFiber(string name, int capacity = 4096, int maxBatchSize = 256, Action<Exception> onError = null)
        {
            Name = name;
            _queue = new BatchQueue<IWorkJob>(capacity);
            _maxBatchSize = Math.Max(1, maxBatchSize);
            _onError = onError;
            
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.Start();
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

            for (var retry = 0; retry < 3; retry++)
            {
                var result = _queue.TryEnqueue(job);

                switch (result)
                {
                    case EnqueueResult.Success:
                        return true;
                
                    case EnqueueResult.Contention:
                        spinner.SpinOnce();
                        continue;
                
                    case EnqueueResult.Full:
                        Thread.Yield();
                        continue;
                
                    case EnqueueResult.Closed:
                        job.Dispose();
                        return false;
                
                    default:
                        return false;
                }
            }

            HandleQueueFull(job);
            return false;
        }

        private void HandleQueueFull(IWorkJob job)
        {
            var ex = new FiberQueueFullException(Name, _queue.PendingCount, _queue.Capacity, _queue.TotalDroppedCount, job);
            _onError?.Invoke(ex);
            
            job.Dispose();
        }

        private void Run()
        {
            var batchBuf = new IWorkJob[_maxBatchSize];

            while (!_disposed)  
            {
                var count = _queue.DequeueBatch(batchBuf);

                if (count == 0)
                {
                    if (_queue.IsClosed) break;
                    _queue.WaitForItem();
                    if (_queue.IsClosed) break;
                    continue;
                }
                
                ExecuteItems(batchBuf, count);
            }
        }

        private void ExecuteItems(IWorkJob[] jobs, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var job = jobs[i];
                try
                {
                    job.Execute();
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref _totalExceptionCount);
                    _onError?.Invoke(new FiberException(Name, job, e.Message, e));
                }
                finally
                {
                    job.Dispose();
                    jobs[i] = null;
                }
            }
        }

        public FiberSnapshot GetSnapshot()
        {
            return new FiberSnapshot
            {
                Name = Name,
                IsWorking = !_disposed,
                QueuePendingCount = _queue.PendingCount,
                QueueCapacity = _queue.Capacity,
                TotalProcessedCount = _queue.TotalProcessedCount,
                TotalDroppedCount = _queue.TotalDroppedCount,
                TotalDequeueCount = _queue.TotalDequeuedCount,
                AvgBatchCount = _queue.AvgBatchSize,
                TotalExceptionCount = Volatile.Read(ref _totalExceptionCount),
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _queue.Close();
            
            if (_thread.IsAlive)
                _thread.Join(1000);
            
            _queue.Dispose();
        }
    }
}
