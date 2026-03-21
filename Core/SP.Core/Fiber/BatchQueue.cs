using System;
using System.Threading;

namespace SP.Core.Fiber
{
    public sealed class BatchQueue<T> : IDisposable where T : class
    {
        private readonly T[] _array;
        private readonly int _mask;
        private readonly int _capacity;
        
        private long _head;
        private long _tail;
        
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private volatile bool _closed;

        public BatchQueue(int capacity = 4096)
        {
            _capacity = NextPowerOfTwo(capacity);
            _mask = _capacity - 1;
            _array = new T[_capacity];
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1; 
            v |= v >> 2; 
            v |= v >> 4; 
            v |= v >> 8; 
            v |= v >> 16;
            return v + 1;
        }

        public bool TryEnqueue(T item, int maxSpinCount = 100)
        {
            if (_closed) return false;

            var spinner = new SpinWait();
            for (var i = 0; i < maxSpinCount; i++)
            {
                var head = Volatile.Read(ref _head);
                var tail = Volatile.Read(ref _tail);

                if (head - tail < _capacity)
                {
                    if (Interlocked.CompareExchange(ref _head, head + 1, head) == head)
                    {
                        Volatile.Write(ref _array[head & _mask], item);
                        if (_signal.CurrentCount == 0) _signal.Release();
                        return true;
                    }
                }
                
                spinner.SpinOnce();
            }
            return false;
        }
        
        public int DequeueBatch(Span<T> buffer)
        {
            var tail = _tail;
            var head = Volatile.Read(ref _head);
            var count = 0;
            
            while (tail < head && count < buffer.Length)
            {
                var index = (int)(tail & _mask);
                var item = Interlocked.Exchange(ref _array[index], null);
                
                if (item == null) break;
                
                buffer[count++] = item;
                tail++;
            }

            if (count > 0) Volatile.Write(ref _tail, tail);
            return count;
        }

        public bool WaitForItem(CancellationToken ct)
        {
            try
            {
                _signal.Wait(ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        public void Dispose()
        {
            _closed = true;
            _signal.Dispose();
        }
    }
}
