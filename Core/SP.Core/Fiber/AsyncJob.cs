using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace SP.Core.Fiber
{
    public class AsyncJob : IAsyncJob
    {
        private static readonly ConcurrentDictionary<MethodInfo, Action<object, object[]>> Cache =
            new ConcurrentDictionary<MethodInfo, Action<object, object[]>>();

        private readonly object[] _args;
        private readonly Action<object, object[]> _invoker;

        private readonly object _target;

        private AsyncJob(object target, Action<object, object[]> invoker, object[] args)
        {
            _target = target;
            _invoker = invoker;
            _args = args;
        }

        public void Invoke()
        {
            _invoker(_target, _args);
        }

        public static IAsyncJob From(Action action)
        {
            return new DelegateJob(action);
        }

        public static IAsyncJob From<T>(Action<T> action, T state)
        {
            return new StateJob<T>(action, state);
        }

        public static IAsyncJob From<T1, T2>(Action<T1, T2> action, T1 state1, T2 state2)
        {
            return new StateJob<T1, T2>(action, state1, state2);
        }

        public static IAsyncJob From(object target, MethodInfo method, params object[] args)
        {
            var invoker = Cache.GetOrAdd(method, BuildInvoker);
            return new DelegateJob(() => invoker(target, args));
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

        private class DelegateJob : IAsyncJob
        {
            private readonly Action _action;

            public DelegateJob(Action action)
            {
                _action = action;
            }

            public void Invoke()
            {
                _action();
            }
        }

        private class StateJob<T> : IAsyncJob
        {
            private readonly Action<T> _run;
            private readonly T _state;

            public StateJob(Action<T> run, T state)
            {
                _run = run;
                _state = state;
            }

            public void Invoke()
            {
                _run(_state);
            }
        }

        private class StateJob<T1, T2> : IAsyncJob
        {
            private readonly Action<T1, T2> _run;
            private readonly T1 _state1;
            private readonly T2 _state2;

            public StateJob(Action<T1, T2> run, T1 state1, T2 state2)
            {
                _run = run;
                _state1 = state1;
                _state2 = state2;
            }

            public void Invoke()
            {
                _run(_state1, _state2);
            }
        }
    }
}
