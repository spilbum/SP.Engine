using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SP.Core.Serialization;

namespace SP.Engine.Runtime.Protocol
{
    public static class ProtocolPool<T> where T : class, IProtocolData, new()
    {
        private const int LocalCapacity = 256;
        [ThreadStatic] private static LocalStack _localPool;

        private class LocalStack
        {
            public readonly T[] Items = new T[LocalCapacity];
            public int Count;
        }
        
        private static readonly ConcurrentQueue<T> _globalQueue = new ConcurrentQueue<T>();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent()
        {
            var localStack = _localPool;
            if (localStack != null && localStack.Count > 0)
            {
                return localStack.Items[--localStack.Count];
            }
            
            return _globalQueue.TryDequeue(out var instance) ? instance : new T();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            
            _localPool ??= new LocalStack();
            
            var localStack = _localPool;
            if (localStack.Count < LocalCapacity)
            {
                localStack.Items[localStack.Count++] = instance;
            }
            else
            {
                _globalQueue.Enqueue(instance);
            }
        }
    }
}
