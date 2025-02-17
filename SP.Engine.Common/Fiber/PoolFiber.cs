using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentQueue<IAsyncJob> _queue = new ConcurrentQueue<IAsyncJob>();
        private readonly Action<Exception> _exceptionHandler;
        private volatile bool _batching;
        private volatile bool _running;

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
            while (!_queue.IsEmpty)
            {
                Task.Delay(10).Wait();
            }
        }

        private void ExecuteBatchJob()
        {
            var batchJobs = GetBatchJobs();
            if (batchJobs.Count == 0) return;

            try
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
            finally
            {
                if (!_queue.IsEmpty)
                {
                    Task.Run(ExecuteBatchJob);
                }
                else
                {
                    _batching = false;
                }
            }
        }
        
        private List<IAsyncJob> GetBatchJobs()
        {
            var batchJobs = new List<IAsyncJob>();
            while (batchJobs.Count < _maxBatchSize && _queue.TryDequeue(out var job))
            {
                batchJobs.Add(job);
            }
            return batchJobs;
        }

        void IExecutionAction.Enqueue(IAsyncJob job)
        {
            _queue.Enqueue(job);
            if (_batching) return;

            lock (_lock)
            {
                if (_batching) return;
                _batching = true;
                Task.Run(ExecuteBatchJob);
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
