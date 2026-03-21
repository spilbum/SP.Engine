using System;
using System.Threading;

namespace SP.Core.Fiber
{
    public sealed class FiberException : Exception
    {
        public IFiber Fiber { get; }
        public IWorkJob Job { get; }

        public FiberException(IFiber fiber, IWorkJob job, string message = "", Exception innerException = null)
            : base(message, innerException)
        {
            Fiber = fiber;
            Job = job;
        }
    }
    
    public sealed class ThreadFiber : IFiber
    {
        private IWorkJob[] _batchBuf;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly int _maxBatch;
        private readonly BatchQueue<IWorkJob> _queue;
        private readonly Thread _thread;
        private readonly Action<FiberException> _onError;
        private volatile bool _disposed;
        private volatile bool _running = true;

        public string Name { get; }

        public ThreadFiber(string name, int maxBatchSize = 1024, int queueCapacity = 4096, Action<FiberException> onError = null)
        {
            Name = name;
            _onError = onError;
            _maxBatch = Math.Max(1, maxBatchSize);
            _queue = new BatchQueue<IWorkJob>(queueCapacity);
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            
            _cts.Cancel();
                
            if (Thread.CurrentThread != _thread)
            {
                if (!_thread.Join(TimeSpan.FromSeconds(3)))
                {
                    Console.WriteLine($"Warning: Fiber '{Name}' shutdown timeout.");
                }
            }
            
            _queue.Dispose();
            _cts.Dispose();
        }
        
        public bool Enqueue(Action action) => Enqueue(AsyncJob.From(action));
        public bool Enqueue<T>(Action<T> action, T state) => Enqueue(AsyncJob.From(action, state));
        public bool Enqueue<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2) => Enqueue(AsyncJob.From(action, s1, s2));
        public bool Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 s1, T2 s2, T3 s3) => Enqueue(AsyncJob.From(action, s1, s2, s3));

        private bool Enqueue(IWorkJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (!_running || _disposed) return false;
            if (_queue.TryEnqueue(job))
                return true;

            var ex = new FiberException(this, job, $"Fiber '{Name}' queue full! Job dropped. Job: {job.Name}");
            _onError?.Invoke(ex);
            return false;
        }

        private void Run()
        {
            var ct = _cts.Token;
            _batchBuf ??= new IWorkJob[_maxBatch];

            while (_running)
            {
                if (!_queue.WaitForItem(ct))
                    break;

                if (!_running)
                    break;

                var buf = _batchBuf;
                var n = _queue.DequeueBatch(buf);

                for (var i = 0; i < n; i++)
                {
                    var job = buf[i];
                    buf[i] = null;

                    try
                    {
                        job.Invoke();
                    }
                    catch (Exception e)
                    {
                        var message = $"Fiber '{Name}' execute failed. Job: {job.Name}, Error: {e.Message}";
                        if (_onError != null)
                        {
                            _onError(new FiberException(this, job, message, e));
                        }
                        else
                        {
                            Console.WriteLine(message);
                        }
                    }
                }
            }
        }
    }
}
