using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SP.Core.Fiber
{
    public static class WorkJob
    {
        public static IWorkJob From(Action action)
        {
            var job = SimplePool<DelegateJob>.Rent();
            job.Init(action);
            return job;
        }

        public static IWorkJob From<T>(Action<T> action, T state)
        {
            var job = SimplePool<StateJob<T>>.Rent();
            job.Init(action, state);
            return job;
        }
        
        public static IWorkJob From<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
        {
            var job = SimplePool<StateJob<T1, T2>>.Rent();
            job.Init(action, state1, state2);
            return job;
        }

        public static IWorkJob From<T1, T2, T3>(Action<T1, T2, T3> action, T1 state1, T2 state2, T3 state3)
        {
            var job = SimplePool<StateJob<T1, T2, T3>>.Rent();
            job.Init(action, state1, state2, state3);
            return job;
        }

        private sealed class DelegateJob : IWorkJob
        {
            private Action _action;
            private int _disposed;
         
            public string Name { get; private set; }

            public void Init(Action action)
            {
                Name = action.Method.Name;
                _action = action;
            }

            public void Execute()
            {
                _action();
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                    return;
                
                Name = null;
                _action = null;
                SimplePool<DelegateJob>.Return(this);
            }
        }
        
        private sealed class StateJob<T> : IWorkJob
        {
            private Action<T> _run;
            private T _state;
            private int _disposed;

            public string Name { get; private set; }

            public void Init(Action<T> run, T state)
            {
                Name = run.Method.Name;
                _run = run;
                _state = state;
            }

            public void Execute()
            {
                _run(_state);
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                    return;

                Name = null;
                _run = null;
                _state = default;
                SimplePool<StateJob<T>>.Return(this);
            }
        }
        
        private sealed class StateJob<T1, T2> : IWorkJob
        {
            private Action<T1, T2> _run;
            private T1 _s1;
            private T2 _s2;
            private int _disposed;

            public string Name { get; private set; }
            
            public void Init(Action<T1, T2> run, T1 s1, T2 s2)
            {
                Name = run.Method.Name;
                _run = run; _s1 = s1; _s2 = s2;
            }

            public void Execute()
            {
                _run(_s1, _s2);
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                    return;
                
                Name = null;
                _run = null;
                _s1 = default;
                _s2 = default;
                SimplePool<StateJob<T1, T2>>.Return(this);
            }
        }

        private sealed class StateJob<T1, T2, T3> : IWorkJob
        {
            private Action<T1, T2, T3> _run;
            private T1 _s1;
            private T2 _s2;
            private T3 _s3;
            private int _disposed;

            public string Name { get; private set; }
            
            public void Init(Action<T1, T2, T3> run, T1 s1, T2 s2, T3 s3)
            {
                Name = run.Method.Name;
                _run = run; _s1 = s1; _s2 = s2; _s3 = s3;
            }

            public void Execute()
            {
                _run(_s1, _s2, _s3);
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                    return;
                
                Name = null;
                _run = null; 
                _s1 = default;
                _s2 = default; 
                _s3 = default;
                SimplePool<StateJob<T1, T2, T3>>.Return(this);
            }
        }
    }
}
