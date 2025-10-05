using System;
using System.Collections.Generic;
using System.Threading;
using SP.Common.Logging;

namespace SP.Common
{
    public interface IBatchQueue<T>
    {
        int Capacity { get; }
        int Count { get; }
        bool Enqueue(T item, int spinBudget = 10_000);
        bool Enqueue(List<T> items, int spinBudget = 10_000);
        bool TryEnqueue(T item);
        bool TryEnqueue(List<T> items);
        bool DequeueAll(List<T> items);
        void Resize(int capacity);
    }
    
    public class ConcurrentBatchQueue<T> : IBatchQueue<T>
    {
        private class Buffer
        {
            public readonly T[] Array;
            public int Claimed;
            public int Published;

            public Buffer(int capacity)
            {
                Array = new T[capacity];
                Claimed = 0;
                Published = 0;
            }

            public void Reset()
            {
                Volatile.Write(ref Claimed, 0);
                Volatile.Write(ref Published, 0);
            }
        }

        private Buffer _active;
        private Buffer _standby;
        private readonly Func<T, bool> _isNull;
        private readonly object _lock = new object();
        private readonly ILogger _logger;
        
        public int Capacity => Volatile.Read(ref _active).Array.Length;
        public int Count => Volatile.Read(ref _active).Published;

        public ConcurrentBatchQueue(int capacity, Func<T, bool> isNull = null, ILogger logger = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _logger = logger;
            _active = new Buffer(capacity);
            _standby = new Buffer(capacity);
            _isNull = isNull ?? (x => EqualityComparer<T>.Default.Equals(x, default));
        }

        public bool Enqueue(T item, int spinBudget = 10_000)
        {
            var spinWait = new SpinWait();
            for (var i = 0; i < spinBudget; i++)
            {
                if (TryEnqueue(item))
                    return true;
                
                spinWait.SpinOnce();
                if ((i & 128) == 0) Thread.Yield();
            }
            return false;
        }

        public bool TryEnqueue(T item)
        {
            var e = Volatile.Read(ref _active);
            var arr = e.Array;
            var claimed = Volatile.Read(ref e.Claimed);

            if (claimed >= arr.Length)
                return false;

            if (Interlocked.CompareExchange(ref e.Claimed, claimed + 1, claimed) != claimed)
                return false;

            arr[claimed] = item;
            Interlocked.Increment(ref e.Published);
            return true;
        }

        public bool Enqueue(List<T> items, int spinBudget = 10_000)
        {
            if (items is null || items.Count == 0) return false;

            var spinWait = new SpinWait();
            for (var i = 0; i < spinBudget; i++)
            {
                if (TryEnqueue(items))
                    return true;
                
                spinWait.SpinOnce();
                if ((i & 128) == 0) Thread.Yield();
            }
            return false;
        }

        public bool TryEnqueue(List<T> items)
        {
            if (items is null || items.Count == 0) return false;
            
            var e = Volatile.Read(ref _active);
            var arr = e.Array;
            var need = items.Count;
            var claimed = Volatile.Read(ref e.Claimed);
            var newClaimed = claimed + need;

            if (newClaimed > arr.Length)
                return false;
                
            if (Interlocked.CompareExchange(ref e.Claimed, newClaimed, claimed) != claimed)
                return false;
                
            for (var i = 0; i < need; i++)
                arr[claimed + i] = items[i];
                
            Interlocked.Add(ref e.Published, need);
            return true;
        }
        
        public bool DequeueAll(List<T> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (!Monitor.TryEnter(_lock)) return false;

            try
            {
                DrainUnlocked(items);
                return true;
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }

        private void DrainUnlocked(List<T> items)
        {
            var prev = Interlocked.Exchange(ref _active, _standby);
            _standby = prev;

            var spinWait = new SpinWait();
            int target;

            while (true)
            {
                target = Volatile.Read(ref prev.Claimed);
                while (Volatile.Read(ref prev.Published) < target)
                {
                    spinWait.SpinOnce();
                    if (spinWait.Count % 128 == 0) Thread.Yield();
                }

                if (Volatile.Read(ref prev.Claimed) == target)
                    break;
            }

            if (target == 0)
            {
                prev.Reset();
                return;
            }

            var arr = prev.Array;
            for (var i = 0; i < target; i++)
            {
                var item = arr[i];
                if (!_isNull(item)) items.Add(item);
                arr[i] = default;
            }
            
            prev.Reset(); 
        }

        public void Resize(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            lock (_lock)
            {
                var newActive = new Buffer(capacity);
                var newStandby = new Buffer(capacity);

                var oldActive = Interlocked.Exchange(ref _active, newActive);
                
                var spin = new SpinWait();
                var target = Volatile.Read(ref oldActive.Claimed);
                while (Volatile.Read(ref oldActive.Published) < target)
                    spin.SpinOnce();
                
                var copy = target <= newStandby.Array.Length ? target: newStandby.Array.Length;
                if (copy > 0)
                {
                    Array.Copy(oldActive.Array, 0, newStandby.Array, 0, copy);
                    Volatile.Write(ref newStandby.Claimed, copy);   
                    Volatile.Write(ref newStandby.Published, copy);
                }
                
                if (target > copy && _logger != null)
                    _logger.Warn("ConcurrentBatchQueue.Resize: truncated {0} items due to smaller capacity (new={1}).",
                        target - copy, capacity);
                
                _standby = newStandby;
                oldActive.Reset();

                _logger?.Info("ConcurrentBatchQueue resized to {0}.", capacity);
            }
        }
    }
}
