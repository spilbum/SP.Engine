using System;
using System.Threading.Tasks;

namespace SP.Common.Fiber
{

    public interface IFiberScheduler
    {
        void Enqueue(Action action);
        void Enqueue<T>(Action<T> action, T param);
        void Enqueue<T1, T2>(Action<T1, T2> action, T1 param1, T2 param2);
        void Enqueue<T1, T2, T3>(Action<T1, T2, T3> action, T1 param1, T2 param2, T3 param3);
        void Enqueue<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 param1, T2 param2, T3 param3, T4 param4);
        void Enqueue<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5);

        IDisposable Schedule(Action action, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T>(Action<T> action, T param, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2>(Action<T1, T2> action, T1 param1, T2 param2, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2, T3>(Action<T1, T2, T3> action, T1 param1, T2 param2, T3 param3, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 param1, T2 param2, T3 param3, T4 param4, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, int dueTimeInMs, int intervalInMs);

        IDisposable Schedule<T1>(Func<T1, Task> action, T1 param1, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2>(Func<T1, T2, Task> action, T1 param1, T2 param2, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2, T3>(Func<T1, T2, T3, Task> action, T1 param1, T2 param2, T3 param3, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2, T3, T4>(Func<T1, T2, T3, T4, Task> action, T1 param1, T2 param2, T3 param3, T4 param4, int dueTimeInMs, int intervalInMs);
        IDisposable Schedule<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5,  Task> action, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, int dueTimeInMs, int intervalInMs);
    }
    
}
