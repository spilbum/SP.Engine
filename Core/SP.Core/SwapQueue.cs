using System;
using System.Buffers;
using System.Collections.Generic;

namespace SP.Core
{
    public sealed class SwapQueue<T> : IDisposable
    {
        private readonly int _capacity;
        private readonly object _syncLock = new object();
        private bool _disposed;
        
        private Node _active;
        private Node _standby;

        public int Count
        {
            get
            {
                lock (_syncLock) return _active.Count;
            }
        }

        public SwapQueue(int capacity)
        {
            _capacity = capacity;
            _active = new Node(capacity);
            _standby = new Node(capacity);
        }

        public bool TryEnqueue(T item)
        {
            if (_disposed) return false;
            lock (_syncLock)
            {
                if (_active.Count >= _capacity)
                    return false;
                
                _active.Array[_active.Count] = item;
                _active.Count++;
                return true;
            }
        }

        public bool TryEnqueue(List<T> items)
        {
            if (_disposed) return false;
            var count = items.Count;
            if (count == 0) return false;

            lock (_syncLock)
            {
                if (_active.Count + count >= _capacity)
                    return false;
                
                Array.Copy(items.ToArray(), 0, _active.Array, _active.Count, count);
                _active.Count += count;
                return true;
            }
        }

        public void Exchange(List<T> destination)
        {
            if (_disposed) return;
            Node nodeToProcess;

            lock (_syncLock)
            {
                if (_active.Count == 0)
                    return;

                nodeToProcess = _active;
                _active = _standby;
            }

            try
            {
                var count = nodeToProcess.Count;

                if (destination.Capacity < count)
                    destination.Capacity = count;

                for (var i = 0; i < count; i++)
                {
                    var item = nodeToProcess.Array[i];
                    if (item == null) continue;
                    destination.Add(item);
                    nodeToProcess.Array[i] = default;
                }
            }
            finally
            {
                nodeToProcess.Reset();
                lock (_syncLock)
                {
                    _standby = nodeToProcess;
                }
            }
        }

        public void Clear()
        {
            lock (_syncLock)
            {
                _active.Reset();
                _standby.Reset();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            lock (_syncLock)
            {
                _active.Dispose();
                _standby.Dispose();
            }
        }
        
        private class Node : IDisposable
        {
            public readonly T[] Array;
            public int Count;
            
            public Node(int capacity) => Array = ArrayPool<T>.Shared.Rent(capacity);

            public void Reset()
            {
                if (Count > 0)
                {
                    System.Array.Clear(Array, 0, Count);
                }
                Count = 0;
            }

            public void Dispose()
            {
                Count = 0;
                if (Array != null)
                    ArrayPool<T>.Shared.Return(Array);
            }
        }
    }
}
