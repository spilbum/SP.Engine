using System.Collections.Concurrent;
using SP.Core.Serialization;

namespace SP.Engine.Runtime.Protocol
{
    public static class ProtocolPool<T> where T : class, IProtocolData, new()
    {
        private static readonly ConcurrentStack<T> _pool = new ConcurrentStack<T>();

        public static T Rent()
        {
            return _pool.TryPop(out var instance) 
                ? instance 
                : new T();
        }

        public static void Return(T instance)
        {
            if (instance == null) return;

            try
            {
                NetSerializer<T>.Reset(instance);
            }
            catch
            {
                return;
            }
            
            _pool.Push(instance);
        }
    }
}
