
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SP.Common.Fiber
{

    internal class BatchQueue
    {
        private readonly object _lock = new object();
        private readonly List<IAsyncJob> _queue = new List<IAsyncJob>();
        private readonly Action<Exception> _exceptionHandler;
        private readonly int _maxBatchSize;
        private volatile bool _running;

        public BatchQueue(int maxBatchSize = 20, Action<Exception> exceptionHandler = null)
        {
            _running = true;
            _maxBatchSize = maxBatchSize;
            _exceptionHandler = exceptionHandler;
        }

        public void Enqueue(IAsyncJob job)
        {
            if (!_running) return;

            lock (_lock)
            {
                _queue.Add(job);
                Monitor.Pulse(_lock); // 대기 중인 스레드 깨우기
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _running = false;
                Monitor.PulseAll(_lock); // 모든 대기 스레드를 깨워 종료 가능하게 함
            }
        }

        public void Run()
        {
            while (true)
            {
                var jobs = GetBatchJobs();
                if (jobs.Count == 0) break;

                ExecuteBatchJobs(jobs);
            }
        }

        private List<IAsyncJob> GetBatchJobs()
        {
            lock (_lock)
            {
                while (_queue.Count == 0)
                {
                    if (!_running) return new List<IAsyncJob>();
                    Monitor.Wait(_lock); // 작업이 추가될 때까지 대기
                }

                // 최대 배치 크기만큼 작업 가져오기
                var batchSize = Math.Min(_maxBatchSize, _queue.Count);
                var jobs = _queue.Take(batchSize).ToList();
                _queue.RemoveRange(0, batchSize);

                return jobs;
            }
        }

        private void ExecuteBatchJobs(List<IAsyncJob> batchJobs)
        {
            foreach (var batchJob in batchJobs)
            {
                try
                {
                    batchJob.Execute(_exceptionHandler);
                }
                catch (Exception ex)
                {
                    _exceptionHandler?.Invoke(ex);
                }
            }
        }
    }
}
