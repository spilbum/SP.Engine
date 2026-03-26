using System;
using System.Collections.Generic;
using System.Threading;

namespace SP.Core
{
    public sealed class SwapQueue<T>
    {
        private readonly int _capacity;
        private Node _active;
        private Node _standby;
        
        public int Count => _active.Head;

        public SwapQueue(int capacity)
        {
            _capacity = capacity;
            _active = new Node(capacity);
            _standby = new Node(capacity);
        }

        public bool TryEnqueue(T item)
        {
            var node = _active;
            
            Interlocked.Increment(ref node.WriterCount);
            if (node != _active)
            {
                Interlocked.Decrement(ref node.WriterCount);
                return false;
            }

            try
            {
                var claimed = Volatile.Read(ref node.Tail);
                if (claimed >= _capacity) return false;
            
                if (Interlocked.CompareExchange(ref node.Tail, claimed + 1, claimed) != claimed)
                    return false;

                node.Array[claimed] = item;
                Interlocked.Increment(ref node.Head);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref node.WriterCount);
            }
        }

        public bool TryEnqueue(List<T> items)
        {
            if (items.Count == 0) return false;
            
            var node = _active;
            Interlocked.Increment(ref node.WriterCount);
            if (node != _active)
            {
                Interlocked.Decrement(ref node.WriterCount);
                return false;
            }

            try
            {
                var count = items.Count;
                int current, next;
                do
                {
                    current = Volatile.Read(ref node.Tail);
                    next = current + count;
                    if (next > _capacity) return false;
                } while (Interlocked.CompareExchange(ref node.Tail, next, current) != current);
            
                for (var i = 0; i < count; i++)
                    node.Array[current + i] = items[i];
            
                Interlocked.Add(ref node.Head, count);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref node.WriterCount);
            }
        }

        public void Exchange(List<T> destination)
        {
            var filledNode = Interlocked.Exchange(ref _active, _standby);
            
            var spin = new SpinWait();
            while (Volatile.Read(ref filledNode.WriterCount) != 0)
                spin.SpinOnce();
            
            var target = filledNode.Tail;
            while (Volatile.Read(ref filledNode.Head) < target)
                spin.SpinOnce();

            for (var i = 0; i < target; i++)
            {
                destination[i] = filledNode.Array[i];
                filledNode.Array[i] = default;
            }
            
            filledNode.Reset();
            _standby = filledNode;
        }

        public void Reset()
        {
            var dummy = new List<T>();
            Exchange(dummy);
        }
        
        private class Node
        {
            public readonly T[] Array;
            public int Tail;
            public int Head;
            public int WriterCount;
            
            public Node(int capacity) => Array = new T[capacity];

            public void Reset()
            {
                Tail = 0;
                Head = 0;
                WriterCount = 0;
            }
        }
    }
}
