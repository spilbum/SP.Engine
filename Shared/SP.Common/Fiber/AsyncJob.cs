using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SP.Common.Logging;

namespace SP.Common.Fiber
{
    internal class AsyncJob : IAsyncJob
    {
        private readonly Action _delegate;
        
        public AsyncJob(object target, MethodInfo method, params object[] args)
        {
            _delegate = CreateDelegate(target, method, args);
        }

        public void Execute(ILogger logger)
        {
            try
            {
                _delegate();
            }
            catch (Exception e)
            {
                logger?.Error(e);
            }
        }

        private static Action CreateDelegate(object target, MethodInfo method, object[] args)
        {
            if (method.GetParameters().Length != args.Length)
                throw new Exception($"Invalid parameter count. method:{method.Name}");
            
            var instanceExpr = Expression.Constant(target);
            var arguments = method.GetParameters()
                .Select((p, i) =>
                    Expression.Convert(
                        Expression.Constant(args[i]), p.ParameterType)
                ).ToArray<Expression>();

            var call = method.IsStatic
                ? Expression.Call(method, arguments)
                : Expression.Call(instanceExpr, method, arguments);
            
            var lambda = Expression.Lambda<Action>(call);
            return lambda.Compile();
        }
    }
}
