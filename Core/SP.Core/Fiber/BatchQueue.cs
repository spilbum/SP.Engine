using System;
using System.Threading;

namespace SP.Core.Fiber
{
    internal struct Slot<T> where T : class
    {
        public T Item;
        public long Seq;
    }
    
    public sealed class BatchQueue<T> : IDisposable where T : class
    {
        private readonly Slot<T>[] _buffer;
        private readonly int _mask;
        private long _head;
        private long _tail;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private volatile bool _closed;
        private volatile bool _disposed;
        
        public bool IsClosed => _closed;
        
        private long _totalEnqueuedCount;
        private long _totalDroppedCount;
        private long _totalProcessedCount;
        private long _totalDequeueCount;

        public int Capacity => _buffer.Length;
        public int PendingCount => (int)(Volatile.Read(ref _tail) - Volatile.Read(ref _head));
        public long TotalEnqueuedCount => Volatile.Read(ref _totalEnqueuedCount);
        public long TotalDroppedCount => Volatile.Read(ref _totalDroppedCount);
        public long TotalProcessedCount => Volatile.Read(ref _totalProcessedCount);
        public long TotalDequeueCount => Volatile.Read(ref _totalDequeueCount);
        public double AvgBatchSize => _totalDequeueCount == 0 
            ? 0 
            : (double)Volatile.Read(ref _totalProcessedCount) / Volatile.Read(ref _totalDequeueCount);
            
        public BatchQueue(int capacity)
        {
            // 2의 배수로 만듬
            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _buffer = new Slot<T>[cap];
            _mask = cap - 1;

            // 초기 시퀀스 설정
            for (var i = 0; i < cap; i++) _buffer[i].Seq = i;
        }

        public bool TryEnqueue(T item, int maxSpinCount = 100)
        {
            if (_closed || _disposed) return false;

            var spinner = new SpinWait();
            var spins = 0;

            while (true)
            {
                var curTail = Volatile.Read(ref _tail);
                var index = (int)(curTail & _mask);
                var seq = Volatile.Read(ref _buffer[index].Seq);

                var diff = seq - curTail;
                if (diff == 0)
                {
                    if (Interlocked.CompareExchange(ref _tail, curTail + 1, curTail) != curTail) 
                        continue;
                    
                    _buffer[index].Item = item;
                    Volatile.Write(ref _buffer[index].Seq, curTail + 1);
                        
                    if (_signal.CurrentCount == 0) _signal.Release();
                    
                    Interlocked.Increment(ref _totalEnqueuedCount);
                    return true;
                }

                if (diff < 0)
                {
                    if (spins++ >= maxSpinCount)
                    {
                        Interlocked.Increment(ref _totalDroppedCount);
                        return false;
                    }
                }

                spinner.SpinOnce();
            }
        }

        public int DequeueBatch(Span<T> destination)
        {
            var curHead = _head;
            var count = 0;
            var maxTake = destination.Length;

            for (var i = 0; i < maxTake; i++)
            {
                var nextHead = curHead + i;
                var index = (int)(nextHead & _mask);
                var seq = Volatile.Read(ref _buffer[index].Seq);

                if (seq - (nextHead + 1) == 0)
                {
                    destination[i] = _buffer[index].Item;
                    _buffer[index].Item = null;
                    Volatile.Write(ref _buffer[index].Seq, nextHead + _buffer.Length);
                    count++;
                }
                else
                {
                    break;
                }
            }

            if (count == 0) 
                return 0;
            
            _head += count;
            Interlocked.Add(ref _totalProcessedCount, count);
            Interlocked.Increment(ref _totalDequeueCount);
            return count;
        }
        
        public void WaitForItem() => _signal.Wait();

        public void Close()
        {
            if (_closed) return;
            _closed = true;

            try
            {
                if (_signal.CurrentCount == 0)
                    _signal.Release();
            }
            catch (ObjectDisposedException)
            {
                
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _closed = true;
            _signal.Dispose();
        }
    }
}
