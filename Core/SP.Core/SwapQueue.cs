using System;
using System.Buffers;
using System.Collections.Generic;

namespace SP.Core
{
    public sealed class SwapQueue<T>
    {
        private readonly int _capacity;
        private readonly object _syncLock = new object();
        
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
                    destination.Add(nodeToProcess.Array[i]);
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
                _active.Clear();
                _standby.Clear();
            }
        }
        
        private class Node
        {
            public readonly T[] Array;
            public int Count;
            
            public Node(int capacity) => Array = ArrayPool<T>.Shared.Rent(capacity);

            public void Reset()
            {
                Count = 0;
            }

            public void Clear()
            {
                Count = 0;
                ArrayPool<T>.Shared.Return(Array);
            }
        }
    }
}
