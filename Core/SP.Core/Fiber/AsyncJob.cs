using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace SP.Core.Fiber
{
    public static class AsyncJob
    {
        private static readonly ConcurrentDictionary<MethodInfo, Action<object, object[]>> Cache =
            new ConcurrentDictionary<MethodInfo, Action<object, object[]>>();

        public static IAsyncJob From(Action action)
        {
            var job = SimplePool<DelegateJob>.Get();
            job.Init(action);
            return job;
        }

        public static IAsyncJob From(Func<Task> action)
        {
            var job = SimplePool<AsyncTaskJob>.Get();
            job.Init(action);
            return job;
        }

        public static IAsyncJob From<T>(Action<T> action, T state)
        {
            var job = SimplePool<StateJob<T>>.Get();
            job.Init(action, state);
            return job;
        }
        
        public static IAsyncJob From<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
        {
            var job = SimplePool<StateJob<T1, T2>>.Get();
            job.Init(action, state1, state2);
            return job;
        }

        public static IAsyncJob From<T1, T2, T3>(Action<T1, T2, T3> action, T1 state1, T2 state2, T3 state3)
        {
            var job = SimplePool<StateJob<T1, T2, T3>>.Get();
            job.Init(action, state1, state2, state3);
            return job;
        }

        public static IAsyncJob From<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 state1, T2 state2, T3 state3, T4 state4)
        {
            var job = SimplePool<StateJob<T1, T2, T3, T4>>.Get();
            job.Init(action, state1, state2, state3, state4);
            return job;
        }

        public static IAsyncJob From(object target, MethodInfo method, params object[] args)
        {
            var invoker = Cache.GetOrAdd(method, BuildInvoker);
            return new ReflectionJob(target, invoker, args);
        }

        private static class SimplePool<T> where T : new()
        {
            private static readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

            public static T Get()
                =>  _queue.TryDequeue(out var item) ? item : new T();
            
            public static void Return(T item)
                => _queue.Enqueue(item);
        }
        
        private class DelegateJob : IAsyncJob
        {
            private Action _action;
            
            public void Init(Action action) => _action = action;

            public void Invoke()
            {
                try
                {
                    _action();
                    _action = null;
                    SimplePool<DelegateJob>.Return(this);
                }
                catch
                {
                    _action = null;
                    throw;
                }
            }
        }
        
        private class AsyncTaskJob : IAsyncJob
        {
            private Func<Task> _action;

            public void Init(Func<Task> action) => _action = action;

            public void Invoke()
            {
                try
                {
                    _action().GetAwaiter().GetResult();
                    _action = null;
                    SimplePool<AsyncTaskJob>.Return(this);
                }
                catch
                {
                    _action = null;
                    throw;
                }
            }
        }

        private class StateJob<T> : IAsyncJob
        {
            private Action<T> _run;
            private T _state;

            public void Init(Action<T> run, T state)
            {
                _run = run;
                _state = state;
            }

            public void Invoke()
            {
                try
                {
                    _run(_state);
                    _run = null;
                    _state = default;
                    SimplePool<StateJob<T>>.Return(this);
                }
                catch
                {
                    _run = null;
                    _state = default;
                    throw;
                }
            }
        }
        
        private class StateJob<T1, T2> : IAsyncJob
        {
            private Action<T1, T2> _run;
            private T1 _s1;
            private T2 _s2;

            public void Init(Action<T1, T2> run, T1 s1, T2 s2)
            {
                _run = run; _s1 = s1; _s2 = s2;
            }

            public void Invoke()
            {
                try
                {
                    _run(_s1, _s2);
                    _run = null; _s1 = default; _s2 = default;
                    SimplePool<StateJob<T1, T2>>.Return(this);
                }
                catch
                {
                    _run = null; _s1 = default; _s2 = default;
                    throw;
                }
            }
        }

        private class StateJob<T1, T2, T3> : IAsyncJob
        {
            private Action<T1, T2, T3> _run;
            private T1 _s1; private T2 _s2; private T3 _s3;

            public void Init(Action<T1, T2, T3> run, T1 s1, T2 s2, T3 s3)
            {
                _run = run; _s1 = s1; _s2 = s2; _s3 = s3;
            }

            public void Invoke()
            {
                try
                {
                    _run(_s1, _s2, _s3);
                    _run = null; _s1 = default; _s2 = default; _s3 = default;
                    SimplePool<StateJob<T1, T2, T3>>.Return(this);
                }
                catch
                {
                    _run = null; _s1 = default; _s2 = default; _s3 = default;
                    throw;
                }
            }
        }

        private class StateJob<T1, T2, T3, T4> : IAsyncJob
        {
            private Action<T1, T2, T3, T4> _run;
            private T1 _s1; private T2 _s2; private T3 _s3; private T4 _s4;

            public void Init(Action<T1, T2, T3, T4> run, T1 s1, T2 s2, T3 s3, T4 s4)
            {
                _run = run; _s1 = s1; _s2 = s2; _s3 = s3; _s4 = s4;
            }

            public void Invoke()
            {
                try
                {
                    _run(_s1, _s2, _s3, _s4);
                    _run = null; _s1 = default; _s2 = default; _s3 = default; _s4 = default;
                    SimplePool<StateJob<T1, T2, T3, T4>>.Return(this);
                }
                catch
                {
                    _run = null; _s1 = default; _s2 = default; _s3 = default; _s4 = default;
                    throw;
                }
            }
        }

        private class ReflectionJob : IAsyncJob
        {
            private readonly object _target;
            private readonly Action<object, object[]> _invoker;
            private readonly object[] _args;

            public ReflectionJob(object target, Action<object, object[]> invoker, params object[] args)
            {
                _target = target;
                _invoker = invoker;
                _args = args;
            }
            
            public void Invoke() => _invoker(_target, _args);
        }
        
        private static Action<object, object[]> BuildInvoker(MethodInfo method)
        {
            var targetParam = Expression.Parameter(typeof(object), "target");
            var argsParam = Expression.Parameter(typeof(object[]), "args");

            var callTarget = method.IsStatic
                ? null
                : Expression.Convert(targetParam, method.DeclaringType!);

            var parameters = method.GetParameters();
            var argsExpr = new Expression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var index = Expression.Constant(i);
                var access = Expression.ArrayIndex(argsParam, index);
                argsExpr[i] = Expression.Convert(access, parameters[i].ParameterType);
            }

            Expression body = method.IsStatic
                ? Expression.Call(method, argsExpr)
                : Expression.Call(callTarget, method, argsExpr);

            var lambda = Expression.Lambda<Action<object, object[]>>(body, targetParam, argsParam);
            return lambda.Compile();
        }
    }
}
