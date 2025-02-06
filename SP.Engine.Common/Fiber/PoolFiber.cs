using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Engine.Common.Fiber
{
    public class PoolFiber : IFiberScheduler, IExecutionAction, IDisposable
    {
        private readonly object _lock = new object();
        private readonly int _maxBatchSize;
        private readonly Scheduler _scheduler;
        private readonly List<IAsyncJob> _queue = new List<IAsyncJob>();
        private readonly Action<Exception> _exceptionHandler;
        private bool _batching;
        private bool _running;

        public PoolFiber(int maxBatchSize = 20, Action<Exception> exceptionHandler = null)
        {
            _scheduler = new Scheduler(this);
            _maxBatchSize = maxBatchSize;
            _exceptionHandler = exceptionHandler;   
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            Enqueue(() => { });
        }

        public void Stop()
        {
            if (!_running) return;
            _scheduler.Dispose();
            _running = false;
        }

        public void Dispose()
        {
            Stop();
        }

        private void ExecuteBatchJob()
        {
            var batchJobs = GetBatchJobs();
            if (batchJobs == null) return;

            try
            {
                foreach (var batchJob in batchJobs)
                {   
                    batchJob.Execute(_exceptionHandler);
                }
            }
            finally
            {
                lock (_lock)
                {
                    if (_queue.Count > 0)
                    {
                        // 실행하는 동안에 큐에 쌓인 작업이 있는 경우
                        ThreadPool.QueueUserWorkItem(_ => ExecuteBatchJob());
                    }
                    else
                    {
                        _batching = false;
                    }
                }
            }
        }
        
        private List<IAsyncJob> GetBatchJobs()
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    _batching = false;
                    return null;
                }

                // 최대 배치 크기만큼 작업 가져오기
                var batchSize = Math.Min(_maxBatchSize, _queue.Count);
                var asyncJobs = _queue.Take(batchSize).ToList();
                _queue.RemoveRange(0, batchSize);

                return asyncJobs;
            }
        }

        void IExecutionAction.Enqueue(IAsyncJob asyncJob)
        {
            lock (_lock)
            {
                if (!_running) return;
                _queue.Add(asyncJob);
                
                if (_batching) return;
                ThreadPool.QueueUserWorkItem(_ => ExecuteBatchJob());
                _batching = true;
            }
        }

        public void Enqueue(Action action)
        {
            (this as IExecutionAction).Enqueue(new AsyncJob(action.Target, action.Method));
        }

        public void Enqueue<T>(Action<T> action, T param)
        {
            (this as IExecutionAction).Enqueue(new AsyncJob(action.Target, action.Method, param));
        }

        public void Enqueue<T1, T2>(Action<T1, T2> action, T1 param1, T2 param2)
        {
            (this as IExecutionAction).Enqueue(new AsyncJob(action.Target, action.Method, param1, param2));
        }

        public void Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 param1, T2 param2, T3 param3)
        {
            (this as IExecutionAction).Enqueue(new AsyncJob(action.Target, action.Method, param1, param2, param3));
        }

        public void Enqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 param1, T2 param2, T3 param3, T4 param4)
        {
            (this as IExecutionAction).Enqueue(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4));
        }

        public void Enqueue<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        {
            (this as IExecutionAction).Enqueue(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4, param5));
        }

        public IDisposable Schedule(Action action, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T>(Action<T> action, T param, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 param1, T2 param2, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2, T3>(Action<T1, T2, T3> action, T1 param1, T2 param2, T3 param3, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 param1, T2 param2, T3 param3, T4 param4, int dueTimeInMs,
            int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5,
            int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4, param5), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1>(Func<T1, Task> action, T1 param1, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2>(Func<T1, T2, Task> action, T1 param1, T2 param2, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2, T3>(Func<T1, T2, T3, Task> action, T1 param1, T2 param2, T3 param3, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2, T3, T4>(Func<T1, T2, T3, T4, Task> action, T1 param1, T2 param2, T3 param3, T4 param4, int dueTimeInMs,
            int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, Task> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5,
            int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4, param5), dueTimeInMs, intervalInMs);
        }
    }
}
