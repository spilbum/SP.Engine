using System;
using System.Reflection;

namespace SP.Engine.Common.Fiber
{
    internal class AsyncJob : IAsyncJob
    {
        private readonly object _instance;
        private readonly MethodInfo _method;
        private readonly object[] _parameters;

        public AsyncJob(object instance, MethodInfo method, params object[] parameters)
        {
            _instance = instance;
            _method = method ?? throw new ArgumentNullException(nameof(method));
            _parameters = parameters;
        }

        public void Execute(Action<Exception> exceptionHandler)
        {
            try
            {
                _method.Invoke(_instance, _parameters);
            }
            catch (TargetInvocationException ex)
            {
                exceptionHandler?.Invoke(ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                exceptionHandler?.Invoke(ex);
            }
        }
    }
}
