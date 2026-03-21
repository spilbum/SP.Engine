using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Core.Fiber
{
    public static class AsyncJob
    {
        private static readonly ConcurrentDictionary<MethodInfo, Action<object, object[]>> Cache =
            new ConcurrentDictionary<MethodInfo, Action<object, object[]>>();

        public static IWorkJob From(Action action)
        {
            var job = SimplePool<DelegateJob>.Get();
            job.Init(action);
            return job;
        }

        public static IWorkJob From<T>(Action<T> action, T state)
        {
            var job = SimplePool<StateJob<T>>.Get();
            job.Init(action, state);
            return job;
        }
        
        public static IWorkJob From<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
        {
            var job = SimplePool<StateJob<T1, T2>>.Get();
            job.Init(action, state1, state2);
            return job;
        }

        public static IWorkJob From<T1, T2, T3>(Action<T1, T2, T3> action, T1 state1, T2 state2, T3 state3)
        {
            var job = SimplePool<StateJob<T1, T2, T3>>.Get();
            job.Init(action, state1, state2, state3);
            return job;
        }

        public static IWorkJob From(object target, MethodInfo method, params object[] args)
        {
            var invoker = Cache.GetOrAdd(method, BuildInvoker);
            var job = SimplePool<ReflectionJob>.Get();
            job.Init(method.Name, target, invoker, args);
            return job;
        }

        private static class SimplePool<T> where T : new()
        {
            private static readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

            public static T Get()
                =>  _queue.TryDequeue(out var item) ? item : new T();
            
            public static void Return(T item)
                => _queue.Enqueue(item);
        }
        
        private sealed class DelegateJob : IWorkJob
        {
            private Action _action;
         
            public string Name { get; private set; }

            public void Init(Action action)
            {
                Name = action.Method.Name;
                _action = action;
            }

            public void Invoke()
            {
                try
                {
                    _action();
                }
                finally
                {
                    _action = null;
                    SimplePool<DelegateJob>.Return(this);
                }
            }
        }
        
        private sealed class StateJob<T> : IWorkJob
        {
            private Action<T> _run;
            private T _state;

            public string Name { get; private set; }

            public void Init(Action<T> run, T state)
            {
                Name = run.Method.Name;
                _run = run;
                _state = state;
            }

            public void Invoke()
            {
                try
                {
                    _run(_state);
                }
                finally
                {
                    _run = null;
                    _state = default;
                    SimplePool<StateJob<T>>.Return(this);
                }
            }
        }
        
        private sealed class StateJob<T1, T2> : IWorkJob
        {
            private Action<T1, T2> _run;
            private T1 _s1;
            private T2 _s2;

            public string Name { get; private set; }
            
            public void Init(Action<T1, T2> run, T1 s1, T2 s2)
            {
                Name = run.Method.Name;
                _run = run; _s1 = s1; _s2 = s2;
            }

            public void Invoke()
            {
                try
                {
                    _run(_s1, _s2);
                }
                finally
                {
                    _run = null; _s1 = default; _s2 = default;
                    SimplePool<StateJob<T1, T2>>.Return(this);
                }
            }
        }

        private sealed class StateJob<T1, T2, T3> : IWorkJob
        {
            private Action<T1, T2, T3> _run;
            private T1 _s1;
            private T2 _s2;
            private T3 _s3;

            public string Name { get; private set; }
            
            public void Init(Action<T1, T2, T3> run, T1 s1, T2 s2, T3 s3)
            {
                Name = run.Method.Name;
                _run = run; _s1 = s1; _s2 = s2; _s3 = s3;
            }

            public void Invoke()
            {
                try
                {
                    _run(_s1, _s2, _s3);
                }
                finally
                {
                    _run = null; _s1 = default; _s2 = default; _s3 = default;
                    SimplePool<StateJob<T1, T2, T3>>.Return(this);
                }
            }
        }

        private sealed class ReflectionJob : IWorkJob
        {
            private object _target;
            private Action<object, object[]> _invoker;
            private object[] _args;

            public string Name { get; private set; }
            
            public void Init(string name, object target, Action<object, object[]> invoker, params object[] args)
            {
                Name = name;
                _target = target;
                _invoker = invoker;
                _args = args;
            }
            
            public void Invoke() 
            { 
                try
                {
                    _invoker(_target, _args);
                }
                finally
                {
                    _invoker = null;
                    _target = null;
                    _args = null;
                    SimplePool<ReflectionJob>.Return(this);
                }
            }
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
