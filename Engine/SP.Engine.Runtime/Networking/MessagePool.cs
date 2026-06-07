using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SP.Engine.Runtime.Networking
{
    internal static class MessagePoolGlobals
    {
        public const int MaxPoolCapacity = 10000;
        private static int _totalPoolCount;
        
        public static int TotalPoolCount => Volatile.Read(ref _totalPoolCount);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Increment() => Interlocked.Increment(ref _totalPoolCount);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Decrement() => Interlocked.Decrement(ref _totalPoolCount);
    }
    public static class MessagePool<T> where T : class, IMessage, new()
    {
        private static readonly ConcurrentQueue<T> _pool = new ConcurrentQueue<T>();
        
        public static T Rent()
        {
            if (!_pool.TryDequeue(out var message))
            {
                return new T();
            }
            
            MessagePoolGlobals.Decrement();
            return message;
        }

        public static void Return(T message)
        {
            if (message == null) return;

            if (MessagePoolGlobals.TotalPoolCount >= MessagePoolGlobals.MaxPoolCapacity)
            {
                // GC에서 정리함
                return;
            }

            _pool.Enqueue(message);
            MessagePoolGlobals.Increment();
        }
    }
}
