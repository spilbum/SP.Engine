
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SP.Common.Logging;

namespace SP.Common.Fiber
{

    internal class BatchQueue
    {
        private readonly ConcurrentQueue<IAsyncJob> _queue = new ConcurrentQueue<IAsyncJob>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly ILogger _logger;
        private readonly int _maxBatchSize;
        private volatile bool _running = true;

        public BatchQueue(int maxBatchSize = 20, ILogger logger = null)
        {
            _maxBatchSize = maxBatchSize;
            _logger = logger;
        }

        public void Enqueue(IAsyncJob job)
        {
            if (!_running) return;
            _queue.Enqueue(job);
            _signal.Release();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _signal.Release();
        }

        public void Run()
        {
            while (_running)
            {
                try
                {
                    _signal.Wait();

                    var batch = new List<IAsyncJob>();
                    while (batch.Count < _maxBatchSize && _queue.TryDequeue(out var job))
                        batch.Add(job);

                    if (batch.Count == 0)
                        continue;

                    foreach (var job in batch)
                        Execute(job);
                }
                catch (Exception e)
                {
                    _logger?.Error(e);
                }
            }

            while (_queue.TryDequeue(out var job))
                Execute(job);
        }

        private void Execute(IAsyncJob job)
        {
            try
            {
                job.Execute(_logger);
            }
            catch (Exception e)
            {
                _logger?.Error(e);
            }
        }
    }
}
