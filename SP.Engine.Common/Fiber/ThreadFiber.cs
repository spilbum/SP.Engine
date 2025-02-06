using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SP.Engine.Common.Logging;

namespace SP.Engine.Common.Fiber
{
    public class ThreadFiber : IFiberScheduler, IExecutionAction, IDisposable
    {
        private static int _globalIndex;
        private readonly BatchQueue _queue;
        private readonly Scheduler _scheduler;
        private readonly Thread _thread;

        public int Index { get; }

        public ThreadFiber(Action<Exception> exceptionHandler = null, ApartmentState apartmentState = ApartmentState.MTA, int maxBatchSize = 50)
        {
            Index = Interlocked.Increment(ref _globalIndex);
            _queue = new BatchQueue(maxBatchSize, exceptionHandler);
            _scheduler = new Scheduler(this);
            _thread = new Thread(_queue.Run)
            {
                IsBackground = true,
                Name = $"ThreadFiber-{Index:D3}",
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _thread.SetApartmentState(apartmentState);
            }
        }

        public void Start()
        {
            _thread.Start();
        }

        public void Dispose()
        {
            _queue.Stop();
            _scheduler.Dispose();
            _thread.Join();
        }
        
        void IExecutionAction.Enqueue(IAsyncJob job)
        {
            _queue.Enqueue(job);
        }
        
        public void Enqueue(Action action)
        {
            _queue.Enqueue(new AsyncJob(action.Target, action.Method));
        }

        public void Enqueue<T>(Action<T> action, T param)
        {
            _queue.Enqueue(new AsyncJob(action.Target, action.Method, param));
        }

        public void Enqueue<T1, T2>(Action<T1, T2> action, T1 param1, T2 param2)
        {
            _queue.Enqueue(new AsyncJob(action.Target, action.Method, param1, param2));
        }

        public void Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 param1, T2 param2, T3 param3)
        {
            _queue.Enqueue(new AsyncJob(action.Target, action.Method, param1, param2, param3));
        }

        public void Enqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 param1, T2 param2, T3 param3, T4 param4)
        {
            _queue.Enqueue(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4));
        }

        public void Enqueue<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5)
        {
            _queue.Enqueue(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4, param5));
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

        public IDisposable Schedule<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 param1, T2 param2, T3 param3, T4 param4, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4), dueTimeInMs, intervalInMs);
        }

        public IDisposable Schedule<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1, param2, param3, param4, param5), dueTimeInMs, intervalInMs);
        }
        
        public IDisposable Schedule<T1>(Func<T1, Task> action, T1 param1, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1), dueTimeInMs, intervalInMs);
        }
        
        public IDisposable Schedule<T1, T2>(Func<T1, T2, Task> action, T1 param1, T2 param2, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1), dueTimeInMs, intervalInMs);
        }
        
        public IDisposable Schedule<T1, T2, T3>(Func<T1, T2, T3, Task> action, T1 param1, T2 param2, T3 param3, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1), dueTimeInMs, intervalInMs);
        }
        
        public IDisposable Schedule<T1, T2, T3, T4>(Func<T1, T2, T3, T4, Task> action, T1 param1, T2 param2, T3 param3, T4 param4, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1), dueTimeInMs, intervalInMs);
        }
        
        public IDisposable Schedule<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, Task> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, int dueTimeInMs, int intervalInMs)
        {
            return _scheduler.Schedule(new AsyncJob(action.Target, action.Method, param1), dueTimeInMs, intervalInMs);
        }
    }
}
