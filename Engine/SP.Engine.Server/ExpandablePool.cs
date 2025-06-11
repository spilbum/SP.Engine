using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SP.Engine.Server
{
    public interface IPoolSegmentFactory<T>
    {
        IPoolSegment Create(int size, out T[] poolItems);
    }

    public interface IPoolSegment
    {
        int Count { get; }
    }

    public interface IObjectPool<T> : IDisposable
    {
        bool TryRent(out T item);
        void Return(T item);
        void Clear();
    }

    public class ExpandablePool<T> : IObjectPool<T>
    {
        private readonly ConcurrentStack<T> _globalStack = new();
        private IPoolSegment[] _itemSources;
        private IPoolSegmentFactory<T> _sourceCreator;

        private int _currentSourceCount;
        private int _isExpanding;
        private bool _isDisposed;

        public int MinPoolSize { get; private set; }
        public int MaxPoolSize { get; private set; }
        public int TotalItemCount { get; private set; }
        public int AvailableItemCount => _globalStack.Count;
        public int CurrentSourceCount => _currentSourceCount;
        public bool IsExpanding => _isExpanding == 1;

        public void Initialize(int minPoolSize, int maxPoolSize, IPoolSegmentFactory<T> sourceCreator)
        {
            ArgumentNullException.ThrowIfNull(sourceCreator);
            if (minPoolSize <= 0 || maxPoolSize < minPoolSize)
                throw new ArgumentException("Invalid min/max pool size.");
            
            MinPoolSize = minPoolSize;
            MaxPoolSize = maxPoolSize;
            _sourceCreator = sourceCreator;
            _itemSources = new IPoolSegment[CalculateSourceArraySize(minPoolSize, maxPoolSize)];
            AddSource(minPoolSize);
        }

        public bool TryRent(out T item)
        {
            item = default;

            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ExpandablePool<T>));
            
            if (_globalStack.TryPop(out item))
                return true;

            return TryEnsureCapacity() && _globalStack.TryPop(out item);
        }
        
        public void Return(T item)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ExpandablePool<T>));
            
            if (item == null) 
                throw new ArgumentNullException(nameof(item));
            
            _globalStack.Push(item);
        }

        public void Clear()
        {
            if (_isDisposed) return;

            while (_globalStack.TryPop(out var item))
            {
                if (item is IDisposable disposable)
                    disposable.Dispose();
            }

            for (var i = 0; i < _currentSourceCount; i++)
                _itemSources[i] = null;

            _itemSources = null;
            _currentSourceCount = 0;
            TotalItemCount = 0;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Clear();
        }
        
        private static int CalculateSourceArraySize(int minPoolSize, int maxPoolSize)
        {
            var size = 1;
            var current = minPoolSize;
            
            while (current < maxPoolSize)
            {
                size++;
                current *= 2;
            }

            return size;
        }

        private void AddSource(int size)
        {
            if (null == _itemSources)
                throw new InvalidOperationException("Item sources cannot be null.");

            var newIndex = Interlocked.Increment(ref _currentSourceCount) - 1;
            if (newIndex >= _itemSources.Length)
                throw new InvalidOperationException("Exceeded max source count.");
            
            _itemSources[newIndex] = _sourceCreator.Create(size, out var items);
            TotalItemCount += items.Length;

            foreach (var item in items)
                _globalStack.Push(item);
        }

        private bool TryEnsureCapacity()
        {
            if (TotalItemCount >= MaxPoolSize || _currentSourceCount >= _itemSources.Length)
                return false;
            
            if (Interlocked.CompareExchange(ref _isExpanding, 1, 0) != 0)
                return false;

            try
            {
                var nextSize = Math.Min(TotalItemCount * 2, MaxPoolSize) - TotalItemCount;
                if (nextSize > 0)
                    AddSource(nextSize);

                return true;
            }
            finally
            {
                _isExpanding = 0;
            }
        }
    }
}
