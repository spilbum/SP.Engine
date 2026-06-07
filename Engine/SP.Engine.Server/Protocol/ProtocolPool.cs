using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using SP.Core.Serialization;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.Protocol
{
    internal static class ProtocolPoolGlobals
    {
        public const int MaxPoolCapacity = 10000;
        private static int _totalPoolCount;
        
        public static int TotalPoolCount => Volatile.Read(ref _totalPoolCount);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Increment() => Interlocked.Increment(ref _totalPoolCount);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Decrement() => Interlocked.Decrement(ref _totalPoolCount);
    }
    
    internal static class ProtocolPool<T> where T : class, IProtocolData, new()
    {
        private static readonly ConcurrentQueue<T> _pool = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Rent()
        {
            if (!_pool.TryDequeue(out var protocol))
            {
                protocol = new T();
                return protocol;
            }
            
            ProtocolPoolGlobals.Decrement();
            return protocol;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(T protocol)
        {
            if (protocol == null) return;

            try
            {
                NetSerializer<T>.Reset(protocol);
            }
            catch
            {
                return;
            }

            if (ProtocolPoolGlobals.TotalPoolCount >= ProtocolPoolGlobals.MaxPoolCapacity)
            {
                return;
            }

            _pool.Enqueue(protocol);
            ProtocolPoolGlobals.Increment();
        }
    }
}
