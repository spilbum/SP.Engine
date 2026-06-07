using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SP.Engine.Server;

public interface IPoolObjectFactory<out T>
{
    T[] Create(int size);
}

public interface IObjectPool<T> : IDisposable
{
    bool TryRent(out T item);
    void Return(T item);
}

public sealed class ExpandablePool<T> : IObjectPool<T>
{
    private readonly ConcurrentStack<T> _globalStack = new();
    private readonly object _expansionLock = new();
    private IPoolObjectFactory<T> _factory;
    
    private int _totalCount;
    private int _minPoolSize;
    private int _maxPoolSize;
    private volatile bool _disposed;
    
    public int TotalCount => Volatile.Read(ref _totalCount);
    
    public void Initialize(int minPoolSize, int maxPoolSize, IPoolObjectFactory<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (minPoolSize <= 0 || maxPoolSize < minPoolSize)
            throw new ArgumentException("Invalid min/max pool size.");

        _minPoolSize = minPoolSize;
        _maxPoolSize = maxPoolSize;
        _factory = factory;
        
        AddSource(minPoolSize);
    }
    
    public bool TryRent(out T item)
    {
        if (_globalStack.TryPop(out item)) return true;

        if (_disposed)
        {
            item = default;
            return false;
        }

        return TryRentWithExpansion(out item);
    }
    
    private bool TryRentWithExpansion(out T item)
    {
        lock (_expansionLock)
        {
            if (_globalStack.TryPop(out item)) return true;
            
            var totalCount = Volatile.Read(ref _totalCount);
            if (totalCount >= _maxPoolSize)
            {
                item = default;
                return false;
            }

            // 확장 사이즈 계산
            var nextSize = Math.Min(totalCount * 2, _maxPoolSize - totalCount);
            if (nextSize <= 0)
            {
                item = default;
                return false;
            }
            
            AddSource(nextSize);
            
            return _globalStack.TryPop(out item);
        }
    }
    
    private void AddSource(int size)
    {
        var items = _factory.Create(size);
        Interlocked.Add(ref _totalCount, items.Length);
        
        foreach (var item in items)
            _globalStack.Push(item);
    }
    
    public void Return(T item)
    {
        if (_disposed || item == null) return;
        _globalStack.Push(item);
    }
    
    public void Dispose()
    {
        if (_disposed) return;

        lock (_expansionLock)
        {
            if (_disposed) return;
            _disposed = true;
        
            while (_globalStack.TryPop(out var item))
            {
                if (item is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }
}
