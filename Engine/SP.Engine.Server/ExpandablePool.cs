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
    private readonly ManualResetEventSlim _expansionEvent = new(true);
    private IPoolObjectFactory<T> _factory;
    
    private int _totalItemCount;
    private int _minPoolSize;
    private int _maxPoolSize;
    private int _expanding;
    private volatile bool _disposed;
    
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
        while (!_disposed)
        {
            if (_globalStack.TryPop(out item)) return true;

            if (TryEnsureCapacity(out var wasExpanding)) continue;

            if (!wasExpanding && Volatile.Read(ref _totalItemCount) >= _maxPoolSize)
            {
                item = default;
                return false;
            }

            _expansionEvent.Wait();
        }
        
        item = default;
        return false;
    }

    public void Return(T item)
    {
        if (_disposed || item == null) return;
        _globalStack.Push(item);
    }
    
    private bool TryEnsureCapacity(out bool wasExpanding)
    {
        wasExpanding = false;
        if (Volatile.Read(ref _totalItemCount) >= _maxPoolSize) return false;

        if (Interlocked.CompareExchange(ref _expanding, 1, 0) != 0)
        {
            wasExpanding = true;
            return false;
        }
        
        _expansionEvent.Reset();

        try
        {
            var total = _totalItemCount;
            if (total >= _maxPoolSize) return false;
            
            var nextSize = Math.Min(total * 2, _maxPoolSize - total);
            if (nextSize <= 0) return false;
            AddSource(nextSize);
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _expanding, 0);
            _expansionEvent.Set();
        }
    }
    
    private void AddSource(int size)
    {
        var items = _factory.Create(size);
        
        Interlocked.Add(ref _totalItemCount, items.Length);
        
        foreach (var item in items)
            _globalStack.Push(item);
    }
    
    public void Dispose()
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
