using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SP.Core.Fiber
{
    public static class WorkJobPool<T> where T : class, IWorkJob, new()
    {
        // 각 스레드 별 512개 캐시만 유지
        private const int LocalCapacity = 512;
        [ThreadStatic] private static LocalPool _localPool;

        private class LocalPool
        {
            public readonly T[] Items = new T[LocalCapacity];
            public int Count;
        }
            
        private static readonly ConcurrentQueue<T> _globalQueue = new ConcurrentQueue<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent()
        {
            var pool = _localPool;
            if (pool != null && pool.Count > 0)
            {
                return pool.Items[--pool.Count];
            }
                
            return _globalQueue.TryDequeue(out var item) ? item : new T();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T item)
        {
            _localPool ??= new LocalPool();
                
            var pool = _localPool;
            if (pool.Count < LocalCapacity)
            {
                pool.Items[pool.Count++] = item;
            }
            else
            {
                _globalQueue.Enqueue(item);
            }
        }
    }

}
