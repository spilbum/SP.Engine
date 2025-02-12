using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SP.Engine.Core
{

public interface ISmartPoolSourceCreator<T>
{
    ISmartPoolSource Create(int size, out T[] poolItems);
}

public interface ISmartPoolSource
{
    int Count { get; }
}

public interface ISmartPool<T>
{
    bool Rent(out T item);
    void Return(T item);
}

 public class SmartPool<T> : ISmartPool<T>
    {
        private readonly ConcurrentStack<T> _globalStack = new ConcurrentStack<T>();
        private ISmartPoolSource[] _itemSources;
        private ISmartPoolSourceCreator<T> _sourceCreator;

        private int _currentSourceCount;
        private int _isIncreasing;

        public int MinPoolSize { get; private set; }
        public int MaxPoolSize { get; private set; }
        public int TotalItemCount { get; private set; }
        public int UsableItemCount => _globalStack.Count;

        public void Initialize(int minPoolSize, int maxPoolSize, ISmartPoolSourceCreator<T> sourceCreator)
        {
            MinPoolSize = minPoolSize;
            MaxPoolSize = maxPoolSize;
            _sourceCreator = sourceCreator ?? throw new ArgumentNullException(nameof(sourceCreator));

            _itemSources = new ISmartPoolSource[CalculateSourceArraySize(minPoolSize, maxPoolSize)];
            AddSource(minPoolSize);
        }

        public void Return(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _globalStack.Push(item);
        }

        public bool Rent(out T item)
        {
            if (_globalStack.TryPop(out item))
                return true;

            return EnsureCapacity() && _globalStack.TryPop(out item);
        }

        private static int CalculateSourceArraySize(int minPoolSize, int maxPoolSize)
        {
            var size = 1;
            var currentValue = minPoolSize;
            while (currentValue < maxPoolSize)
            {
                size++;
                currentValue *= 2;
            }
            return size;
        }

        private void AddSource(int size)
        {
            if (null == _itemSources)
                throw new InvalidOperationException("Item sources cannot be null.");
            
            _itemSources[_currentSourceCount++] = _sourceCreator.Create(size, out var items);
            TotalItemCount += size;

            foreach (var item in items)
                _globalStack.Push(item);
        }

        private bool EnsureCapacity()
        {
            if (null == _itemSources)
                throw new InvalidOperationException("Item sources cannot be null.");
            
            if (_currentSourceCount >= _itemSources.Length || _isIncreasing == 1)
                return false;

            if (Interlocked.CompareExchange(ref _isIncreasing, 1, 0) != 0)
                return false;

            try
            {
                var itemsCount = Math.Min(TotalItemCount, MaxPoolSize - TotalItemCount);
                if (itemsCount > 0)
                    AddSource(itemsCount);

                return true;
            }
            finally
            {
                _isIncreasing = 0;
            }
        }
    }

    
}
