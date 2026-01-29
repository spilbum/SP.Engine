using System;
using System.Threading;
using SP.Core.Logging;

namespace SP.Core.Fiber
{
    public class ThreadFiber : IFiber, IDisposable
    {
        [ThreadStatic] private static IAsyncJob[] _batchBuf;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ILogger _logger;
        private readonly int _maxBatch;
        private readonly BatchQueue<IAsyncJob> _queue;
        private readonly Thread _thread;
        private volatile bool _disposed;
        private volatile bool _running = true;

        public ThreadFiber(ILogger logger, string name, int maxBatchSize = 1024, int queueCapacity = -1)
        {
            _maxBatch = Math.Max(1, maxBatchSize);
            _queue = new BatchQueue<IAsyncJob>(queueCapacity);
            _logger = logger;
            _thread = new Thread(Run) { IsBackground = true, Name = name };
            _thread.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            _queue.Close();
            _cts.Cancel();

            if (Thread.CurrentThread != _thread)
                try
                {
                    _thread.Join();
                }
                catch
                {
                    /* ignore */
                }
        }

        public bool TryEnqueue(Action action)
            => TryEnqueue(AsyncJob.From(action));

        public bool TryEnqueue<T>(Action<T> action, T state)
            => TryEnqueue(AsyncJob.From(action, state));

        public bool TryEnqueue<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
            => TryEnqueue(AsyncJob.From(action, state1, state2));
        
        public bool TryEnqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 state1, T2 state2, T3 state3)
            => TryEnqueue(AsyncJob.From(action, state1, state2, state3));
        
        public bool TryEnqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 state1, T2 state2, T3 state3, T4 state4)
            => TryEnqueue(AsyncJob.From(action, state1, state2, state3, state4));

        public bool TryEnqueue(IAsyncJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (!_running || _disposed) return false;
            if (_queue.TryEnqueue(job, spinBudge: 1000))
            {
                return true;
            }
            
            _logger.Error("ThreadFiber queue full! Job dropped.");
            return false;
        }

        private void Run()
        {
            var ct = _cts.Token;
            _batchBuf ??= new IAsyncJob[_maxBatch];

            try
            {
                while (_running || _queue.Count > 0)
                {
                    if (_queue.Count == 0)
                    {
                        try
                        {
                            _queue.WaitForItem(ct);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        if (!_running && _queue.Count == 0)
                            break;
                    }

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
                            _logger.Error(e, "ThreadFiber batch failed");
                        }
                    }
                }
            }
            finally
            {
                _running = false;
                _queue.Close();
                _cts.Cancel();
            }
        }
    }
}
