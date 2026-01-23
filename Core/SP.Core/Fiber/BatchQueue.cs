using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SP.Core.Fiber
{
    public sealed class BatchQueue<T>
    {
        private readonly int _capacity;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private volatile bool _closed;
        private int _count;

        public BatchQueue(int capacity = -1)
        {
            _capacity = capacity;
        }

        public int Count => _capacity >= 0 ? Volatile.Read(ref _count) : _queue.Count;

        public bool TryEnqueue(T item)
        {
            if (_closed) return false;
            if (_capacity >= 0)
            {
                var after = Interlocked.Increment(ref _count);
                if (after > _capacity)
                {
                    Interlocked.Decrement(ref _count);
                    return false;
                }
            }

            _queue.Enqueue(item);
            _signal.Release();
            return true;
        }

        public int DequeueBatch(Span<T> buffer)
        {
            var n = 0;
            while (n < buffer.Length && _queue.TryDequeue(out var item))
            {
                buffer[n++] = item;
                if (_capacity >= 0) Interlocked.Decrement(ref _count);
            }

            return n;
        }

        public void WaitForItem(CancellationToken ct)
        {
            _signal.Wait(ct);
        }

        public void Close()
        {
            _closed = true;
            _signal.Dispose();
        }
    }
}
