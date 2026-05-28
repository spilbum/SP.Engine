using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SP.Core.Fiber
{
    public static class SimplePool<T> where T : new()
    {
        // 각 스레드 별 512개 캐시만 유지
        private const int LocalCapacity = 512;

        [ThreadStatic] private static LocalStack _local;

        private class LocalStack
        {
            public readonly T[] Items = new T[LocalCapacity];
            public int Count;
        }
            
        private static readonly ConcurrentQueue<T> _global = new ConcurrentQueue<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent()
        {
            var local = _local;
            if (local != null && local.Count > 0)
            {
                return local.Items[--local.Count];
            }
                
            return _global.TryDequeue(out var item) ? item : new T();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T item)
        {
            _local ??= new LocalStack();
                
            var local = _local;
            if (local.Count < LocalCapacity)
            {
                local.Items[local.Count++] = item;
            }
            else
            {
                _global.Enqueue(item);
            }
        }
    }

}
