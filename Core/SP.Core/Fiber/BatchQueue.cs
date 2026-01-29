using System;
using System.Threading;

namespace SP.Core.Fiber
{
    public sealed class BatchQueue<T> : IDisposable where T : class
    {
        private readonly T[] _array;
        private readonly int _capacity;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private long _head;
        private long _tail;
        private volatile bool _closed;

        public BatchQueue(int capacity = 1000)
        {
            if (capacity <= 0) capacity = 1000;
            _capacity = capacity;
            _array = new T[capacity];
        }

        public int Count => (int)(Volatile.Read(ref _head) - Volatile.Read(ref _tail));

        public bool TryEnqueue(T item, int spinBudge)
        {
            var spin = new SpinWait();

            for (var i = 0; i < spinBudge; i++)
            {
                if (TryEnqueue(item)) return true;
                spin.SpinOnce();
            }
            
            return false;
        }
        
        private bool TryEnqueue(T item)
        {
            if (_closed) return false;
            
            var head = Volatile.Read(ref _head);
            var tail = Volatile.Read(ref _tail);

            if (head - tail >= _capacity) return false;
            
            if (Interlocked.CompareExchange(ref _head, head + 1, head) != head)
                return false;

            _array[head % _capacity] = item;

            if (_signal.CurrentCount != 0) return true;
            
            try { _signal.Release(); } catch { /* ignore */ }
            return true;
        }

        public int DequeueBatch(Span<T> buffer)
        {
            var tail = _tail;
            var head = Volatile.Read(ref _head);

            var count = 0;
            while (tail < head && count < buffer.Length)
            {
                var index = tail % _capacity;
                var item = _array[index];
                if (item == null) break;
                buffer[count++] = item;
                _array[index] = null;
                tail++;
            }

            if (count > 0) Volatile.Write(ref _tail, tail);
            return count;
        }

        public void WaitForItem(CancellationToken ct) => _signal.Wait(ct);
        public void Close()
        {
            _closed = true;
            _signal.Dispose();
        }
        
        public void Dispose() => Close();
    }
}
