
using System;
using System.Threading;
using SP.Common.Logging;

namespace SP.Engine.Client
{
    using System.Collections.Generic;
    
    public class ConcurrentBatchQueue<T>
    {
        private sealed class Entity
        {
            public T[] Array;
            public int Count;
        }

        private volatile Entity _entity;
        private volatile Entity _backup;        
        private readonly T _null = default;
        private readonly Func<T, bool> _nullValidator;
        private readonly object _lock = new object();
        private readonly ILogger _logger;
        private long _count;

        public int Count => (int)Interlocked.Read(ref _count);

        public bool IsEmpty => Count == 0;

        public ConcurrentBatchQueue(int capacity, Func<T, bool> validator = null, ILogger logger = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
            
            _logger = logger;
            _entity = new Entity { Array = new T[capacity] };
            _backup = new Entity { Array = new T[capacity] };
            _nullValidator = validator ?? (item => EqualityComparer<T>.Default.Equals(item, default));
        }

        public bool Enqueue(T item)
        {
            var spinWait = new SpinWait();
            for (var i = 0; i < 10; i++)
            {
                if (TryEnqueue(item, out var isFull))
                    return true;

                if (isFull)
                    return false;

                spinWait.SpinOnce();
            }
            return false;
        }

        private bool TryEnqueue(T item, out bool isFull)
        {
            isFull = false;
            var entity = _entity;
            var array = _entity.Array;
            var count = entity.Count;

            if (array == null)
                return false;
            
            if (count >= array.Length)
            {
                isFull = true;
                return false;
            }

            var newCount = count + 1;
            var oldCount = Interlocked.CompareExchange(ref entity.Count, newCount, count);
            if (oldCount != count)
                return false;

            array[oldCount] = item;
            Interlocked.Exchange(ref _count, newCount);
            return true;
        }

        public bool TryDequeue(ref List<T> items)
        {
            var entity = _entity;
            var array = _entity.Array;
            if (entity.Count == 0 || array == null)
                return false;

            if (_backup.Array.Length != array.Length)
                _backup = new Entity { Array = new T[array.Length] };

            _backup = Interlocked.Exchange(ref _entity, _backup);

            for (var i = 0; i < _backup.Count; i++)
            {
                var item = _backup.Array[i];
                if (_nullValidator(item)) 
                    continue;
                
                items.Add(item);
                _backup.Array[i] = _null;
            }

            entity.Count = 0;
            Interlocked.Exchange(ref _count, 0);
            return true;
        }

        /// <summary>
        /// Resizes the queue to a new capacity.
        /// </summary>
        /// <param name="newCapacity">The new capacity for the queue.</param>
        public void Resize(int newCapacity)
        {
            if (newCapacity <= 0) 
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "Capacity must be greater than zero.");

            lock (_lock)
            {
                var entity = _entity;
                var array = _entity.Array;
                if (array == null || newCapacity == array.Length)
                    return;
                
                var newArray = new T[newCapacity];
                Array.Copy(array, newArray, Math.Min(entity.Count, newCapacity));

                _backup = new Entity { Array = new T[newCapacity], Count = 0};
                _entity = new Entity { Array = newArray, Count = Math.Min(entity.Count, newCapacity) };
                Interlocked.Exchange(ref _count, _entity.Count);
                _logger?.Info($"Resized queue to {newCapacity} (prev={array.Length}, kept={entity.Count}");
            }
        }
    }
}
