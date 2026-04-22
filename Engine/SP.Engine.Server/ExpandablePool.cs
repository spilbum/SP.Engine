using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using SP.Engine.Server.Logging;

namespace SP.Engine.Server;

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

public sealed class ExpandablePool<T> : IObjectPool<T>
{
    private readonly ConcurrentStack<T> _globalStack = new();

    private IPoolSegment[] _itemSources;
    private IPoolSegmentFactory<T> _sourceCreator;
    
    private int _currentSourceCount;
    private int _totalItemCount;
    private int _minPoolSize;
    private int _maxPoolSize;
    private int _expanding;
    private volatile bool _disposed;
    
    public void Initialize(int minPoolSize, int maxPoolSize, IPoolSegmentFactory<T> sourceCreator)
    {
        ArgumentNullException.ThrowIfNull(sourceCreator);
        if (minPoolSize <= 0 || maxPoolSize < minPoolSize)
            throw new ArgumentException("Invalid min/max pool size.");

        _minPoolSize = minPoolSize;
        _maxPoolSize = maxPoolSize;
        _sourceCreator = sourceCreator;
        
        _itemSources = new IPoolSegment[CalculateSourceArraySize(minPoolSize, maxPoolSize)];
        AddSource(minPoolSize);
    }
    
    public bool TryRent(out T item)
    {
        if (_disposed)
        {
            item = default;
            return false;
        }
        
        if (_globalStack.TryPop(out item)) return true;
        return TryEnsureCapacity() && _globalStack.TryPop(out item);
    }

    public void Return(T item)
    {
        if (_disposed || item == null) return;
        _globalStack.Push(item);
    }
    
    private bool TryEnsureCapacity()
    {
        if (Volatile.Read(ref _totalItemCount) >= _maxPoolSize) return false;

        if (Interlocked.CompareExchange(ref _expanding, 1, 0) != 0) return false;

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
        }
    }
    
    private void AddSource(int size)
    {
        var newIndex = Interlocked.Increment(ref _currentSourceCount) - 1;
        if (newIndex >= _itemSources.Length) return;

        var segment = _sourceCreator.Create(size, out var items);
        _itemSources[newIndex] = segment;

        Interlocked.Add(ref _totalItemCount, items.Length);

        foreach (var item in items)
            _globalStack.Push(item);
    }
    
    private static int CalculateSourceArraySize(int minPoolSize, int maxPoolSize)
    {
        var count = 1;
        var current = minPoolSize;
        while (current < maxPoolSize)
        {
            count++;
            current *= 2;
        }

        return count;
    }

    public void Clear()
    {
        while (_globalStack.TryPop(out var item))
        {
            if (item is IDisposable disposable)
                disposable.Dispose();
        }

        for (var i = 0; i < _currentSourceCount; i++)
            _itemSources[i] = null;

        _currentSourceCount = 0;
        _totalItemCount = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}
