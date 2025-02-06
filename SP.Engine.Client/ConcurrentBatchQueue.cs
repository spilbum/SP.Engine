
using System;
using System.Threading;

namespace SP.Engine.Client
{
    using System.Collections.Generic;
    
    public class ConcurrentBatchQueue<T>
    {
        private class Entity
        {
            public T[] Array;
            public int Count;
        }

        private Entity _entity;
        private Entity _backup;
        private readonly T _null = default;
        private readonly Func<T, bool> _nullValidator;

        public ConcurrentBatchQueue(int capacity, Func<T, bool> validator = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
            
            _entity = new Entity { Array = new T[capacity] };
            _backup = new Entity { Array = new T[capacity] };
            _nullValidator = validator ?? (item => item == null);
        }

        public bool Enqueue(T item)
        {
            for (var i = 0; i < 10; i++)
            {
                if (TryEnqueue(item, out var isFull) || isFull)
                    return !isFull;
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

            var oldCount = Interlocked.CompareExchange(ref entity.Count, count + 1, entity.Count);
            if (oldCount != count)
                return false;

            array[oldCount] = item;
            return true;
        }

        public bool TryDequeue(ref List<T> items)
        {
            var entity = _entity;
            var array = _entity.Array;
            if (entity.Count == 0 || array == null)
                return false;

            Interlocked.Exchange(ref _entity, _backup);
            for (var i = 0; i < entity.Count; i++)
            {
                var item = array[i];
                if (_nullValidator(item)) 
                    continue;
                
                items.Add(item);
                array[i] = _null;
            }

            entity.Count = 0;
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

            lock (_entity)
            {
                var entity = _entity;
                var array = _entity.Array;
                if (array == null || newCapacity == array.Length)
                    return;

                var newArray = new T[newCapacity];
                Array.Copy(array, newArray, Math.Min(entity.Count, newCapacity));

                _entity = new Entity { Array = newArray, Count = Math.Min(entity.Count, newCapacity) };
                _backup = new Entity { Array = new T[newCapacity] };
            }
        }
        
        public int Count => _entity.Count;
        public bool IsEmpty => _entity.Count == 0;
    }


}
