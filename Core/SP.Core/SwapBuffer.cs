using System;
using System.Collections.Generic;
using System.Threading;

namespace SP.Core
{
    public sealed class SwapBuffer<T>
    {
        private readonly int _capacity;
        private Node _active;
        private Node _standby;

        public SwapBuffer(int capacity)
        {
            _capacity = capacity;
            _active = new Node(capacity);
            _standby = new Node(capacity);
        }

        public bool TryWrite(T item)
        {
            var node = _active;
            
            Interlocked.Increment(ref node.Pending);
            if (node != _active)
            {
                Interlocked.Decrement(ref node.Pending);
                return false;
            }

            try
            {
                var claimed = Volatile.Read(ref node.Claimed);
                if (claimed >= _capacity) return false;
            
                if (Interlocked.CompareExchange(ref node.Claimed, claimed + 1, claimed) != claimed)
                    return false;

                node.Array[claimed] = item;
                Interlocked.Increment(ref node.Published);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref node.Pending);
            }
        }

        public bool TryWriteBatch(List<T> items)
        {
            if (items.Count == 0) return false;
            
            var node = _active;
            Interlocked.Increment(ref node.Pending);
            if (node != _active)
            {
                Interlocked.Decrement(ref node.Pending);
                return false;
            }

            try
            {
                var count = items.Count;
                int current, next;
                do
                {
                    current = Volatile.Read(ref node.Claimed);
                    next = current + count;
                    if (next > _capacity) return false;
                } while (Interlocked.CompareExchange(ref node.Claimed, next, current) != current);
            
                for (var i = 0; i < count; i++)
                    node.Array[current + i] = items[i];
            
                Interlocked.Add(ref node.Published, count);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref node.Pending);
            }
        }

        public void Flush(List<T> outputList)
        {
            // 스왑 (Active -> Standby)
            var filled = Interlocked.Exchange(ref _active, _standby);
            
            var spin = new SpinWait();
            while (Volatile.Read(ref filled.Pending) != 0)
                spin.SpinOnce();
            
            var target = filled.Claimed;
            while (Volatile.Read(ref filled.Published) < target)
                spin.SpinOnce();

            if (target > 0)
            {
                for (var i = 0; i < target; i++)
                {
                    outputList.Add(filled.Array[i]);
                    filled.Array[i] = default;
                }
            }
            
            filled.Reset();
            _standby = filled;
        }
        
        private class Node
        {
            public readonly T[] Array;
            public int Claimed;
            public int Published;
            public int Pending;
            
            public Node(int capacity) => Array = new T[capacity];

            public void Reset()
            {
                Claimed = 0;
                Published = 0;
                Pending = 0;
            }
        }
    }
}
