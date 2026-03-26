using System;
using System.Threading;

namespace SP.Core.Fiber
{
    public sealed class FiberException : Exception
    {
        public IFiber Fiber { get; }
        public IWorkJob Job { get; }

        public FiberException(IFiber fiber, IWorkJob job = null, string message = "", Exception innerException = null)
            : base(message, innerException)
        {
            Fiber = fiber;
            Job = job;
        }
    }

    public sealed class ThreadFiber : IFiber
    {
        private readonly BatchQueue<IWorkJob> _queue;
        private readonly Action<FiberException> _onError;
        private readonly int _maxBatchSize;
        private readonly Thread _thread;
        private volatile bool _disposed;
        
        public string Name { get; }
        public bool IsDisposed => _disposed;

        private long _totalExceptionCount;

        public ThreadFiber(string name, int capacity = 4096, int maxBatchSize = 256, Action<FiberException> onError = null)
        {
            Name = name;
            _queue = new BatchQueue<IWorkJob>(capacity);
            _maxBatchSize = Math.Max(1, maxBatchSize);
            _onError = onError;
            
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.Start();
        }
        
        public bool Enqueue(Action action) => Enqueue(AsyncJob.From(action));
        public bool Enqueue<T>(Action<T> action, T state) => Enqueue(AsyncJob.From(action, state));
        public bool Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2) => Enqueue(AsyncJob.From(action, s1, s2));
        public bool Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3) => Enqueue(AsyncJob.From(action, s1, s2, s3));

        private bool Enqueue(IWorkJob job)
        {
            if (job == null) return false;
            if (_disposed) return false;
            
            try
            {
                if (!_queue.TryEnqueue(job))
                {
                    throw new FiberException(this, job, $"Fiber '{Name}' queue full! Job dropped. Job: {job.Name}");
                }
                
                return true;
            }
            catch (FiberException ex)
            {
                _onError?.Invoke(ex);
                return false;
            }
            catch (Exception ex)
            {
                _onError?.Invoke(new FiberException(this, job, ex.Message, ex));
                return false;
            }
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
                    _onError?.Invoke(new FiberException(this, job, e.Message, e));
                }
                finally
                {
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
                TotalDequeueCount = _queue.TotalDequeueCount,
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
