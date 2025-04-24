using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace SP.Engine.Core.Utilities
{

    public sealed class SendingQueue : IList<ArraySegment<byte>>
    {
        private readonly int _offset;
        private readonly int _capacity;
        private int _currentCount;
        private readonly ArraySegment<byte>[] _globalQueue;
        private static readonly ArraySegment<byte> Null = default;
        private int _updatingCount;
        private int _innerOffset;

        public ushort TrackId { get; private set; } = 1;
        public int Count => _currentCount - _innerOffset;
        public bool IsReadOnly { get; private set; }
        
        public int Position { get; private set; }

        public SendingQueue(ArraySegment<byte>[] globalQueue, int offset, int capacity)
        {
            _globalQueue = globalQueue;
            _offset = offset;
            _capacity = capacity;
        }

        public void SetPosition(int position)
        {
            Position = position;
        }

        private bool TryEnqueue(ArraySegment<byte> item, out bool conflict, ushort trackId)
        {
            conflict = false;
            var oldCount = _currentCount;

            if (oldCount >= _capacity || IsReadOnly || trackId != TrackId)
                return false;

            var updatedCount = Interlocked.CompareExchange(ref _currentCount, oldCount + 1, oldCount);
            if (updatedCount != oldCount)
            {
                conflict = true;
                return false;
            }

            _globalQueue[_offset + oldCount] = item;
            return true;
        }

        public bool Enqueue(ArraySegment<byte> item, ushort trackId)
        {
            if (IsReadOnly)
                return false;

            Interlocked.Increment(ref _updatingCount);

            while (!IsReadOnly)
            {
                if (TryEnqueue(item, out var conflict, trackId))
                {
                    Interlocked.Decrement(ref _updatingCount);
                    return true;
                }

                if (!conflict)
                    break;
            }

            Interlocked.Decrement(ref _updatingCount);
            return false;
        }

        public void StopEnqueue()
        {
            if (IsReadOnly)
                return;

            IsReadOnly = true;

            var spinWait = new SpinWait();
            while (_updatingCount > 0)
                spinWait.SpinOnce();
        }

        public void StartEnqueue()
        {
            IsReadOnly = false;
        }

        public void Clear()
        {
            TrackId = TrackId >= ushort.MaxValue ? (ushort)1 : (ushort)(TrackId + 1);

            for (var i = 0; i < _currentCount; i++)
                _globalQueue[_offset + i] = Null;

            _currentCount = 0;
            _innerOffset = 0;
            Position = 0;
        }

        public int IndexOf(ArraySegment<byte> item) => throw new NotSupportedException();
        public void Insert(int index, ArraySegment<byte> item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        public void Add(ArraySegment<byte> item) => throw new NotSupportedException();
        public bool Remove(ArraySegment<byte> item) => throw new NotSupportedException();
        public bool Contains(ArraySegment<byte> item) => throw new NotSupportedException();

        public void CopyTo(ArraySegment<byte>[] array, int arrayIndex)
        {
            for (var i = 0; i < Count; i++)
            {
                if (array.Length <= arrayIndex + i)
                    continue;

                array[arrayIndex + i] = this[i];
            }
        }

        public ArraySegment<byte> this[int index]
        {
            get => _globalQueue[_offset + _innerOffset + index];
            set => throw new NotSupportedException();
        }

        public IEnumerator<ArraySegment<byte>> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return _globalQueue[_offset + _innerOffset + i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        public void InternalTrim(int offset)
        {
            if (offset <= 0 || _currentCount <= _innerOffset)
                return;

            var accumulatedSize = 0;

            for (var i = _innerOffset; i < _currentCount; i++)
            {
                var segment = _globalQueue[_offset + i];
                accumulatedSize += segment.Count;

                if (accumulatedSize <= offset)
                    continue;

                // Adjust the segment with remaining data
                var excess = accumulatedSize - offset;
                _innerOffset = i;

                // Update the current segment with the leftover data
                if (segment.Array != null)
                {
                    _globalQueue[_offset + i] = new ArraySegment<byte>(
                        segment.Array,
                        segment.Offset + segment.Count - excess,
                        excess
                    );
                }
                    
                return;
            }

            // If the offset exceeds the total size, clear the queue
            Clear();
        }
    }

    public class SendingQueueSourceCreator : ISmartPoolSourceCreator<SendingQueue>
    {
        private readonly int _sendingQueueSize;

        public SendingQueueSourceCreator(int sendingQueueSize)
        {
            if (sendingQueueSize <= 0)
                throw new ArgumentException("Sending queue size must be greater than zero.", nameof(sendingQueueSize));

            _sendingQueueSize = sendingQueueSize;
        }

        public ISmartPoolSource Create(int size, out SendingQueue[] poolItems)
        {
            if (size <= 0)
                throw new ArgumentException("Size must be greater than zero.", nameof(size));

            var source = new ArraySegment<byte>[size * _sendingQueueSize];
            poolItems = new SendingQueue[size];

            for (var i = 0; i < size; i++)
                poolItems[i] = new SendingQueue(source, i * _sendingQueueSize, _sendingQueueSize);

            return new SendingPoolSource(source, size);
        }
    }

    public class SendingPoolSource : ISmartPoolSource
    {
        public ArraySegment<byte>[] Source { get; }
        public int Count { get; }

        public SendingPoolSource(ArraySegment<byte>[] source, int count)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Count = count;
        }
    }
}
