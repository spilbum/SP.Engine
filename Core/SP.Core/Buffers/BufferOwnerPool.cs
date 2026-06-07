using System.Collections.Concurrent;
using System.Threading;

namespace SP.Core.Buffers
{
    public static class BufferOwnerPool
    {
        private static readonly ConcurrentQueue<BufferOwner> _pool = new ConcurrentQueue<BufferOwner>();
        private static int _poolCount;
        private const int MaxPoolCapacity = 10000;

        public static BufferOwner Rent(int capacity)
        {
            if (!_pool.TryDequeue(out var buffer))
            {
                return new BufferOwner(capacity);
            }
            
            Interlocked.Decrement(ref _poolCount);
            buffer.Initialize(capacity);
            return buffer;
        }

        internal static void Return(BufferOwner bufferOwner)
        {
            if (_poolCount >= MaxPoolCapacity)
            {
                return;
            }
            
            _pool.Enqueue(bufferOwner);
            Interlocked.Increment(ref _poolCount);
        }
    }
}
