using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace SP.Engine.Server
{
    public sealed class SegmentQueue(ArraySegment<byte>[] globalQueue, int offset, int capacity)
        : IList<ArraySegment<byte>>
    {
        private int _currentCount;
        private static readonly ArraySegment<byte> Null = default;
        private int _updatingCount;
        private int _innerOffset;
        private bool _isEnqueueBlocked;

        public ushort TrackId { get; private set; } = 1;
        public int Count => _currentCount - _innerOffset;
        public int Position { get; private set; }
        
        bool ICollection<ArraySegment<byte>>.IsReadOnly => true;

        public void SetPosition(int position)
        {
            Position = position;
        }

        private bool TryEnqueue(ArraySegment<byte> item, out bool conflict, ushort trackId)
        {
            conflict = false;
            var oldCount = _currentCount;

            if (oldCount >= capacity || _isEnqueueBlocked || trackId != TrackId)
                return false;

            var updatedCount = Interlocked.CompareExchange(ref _currentCount, oldCount + 1, oldCount);
            if (updatedCount != oldCount)
            {
                conflict = true;
                return false;
            }

            globalQueue[offset + oldCount] = item;
            return true;
        }

        public bool Enqueue(ArraySegment<byte> item, ushort trackId)
        {
            if (_isEnqueueBlocked)
                return false;

            Interlocked.Increment(ref _updatingCount);

            while (!_isEnqueueBlocked)
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
            _isEnqueueBlocked = true;
            var spinWait = new SpinWait();
            while (_updatingCount > 0)
                spinWait.SpinOnce();
        }

        public void StartEnqueue()
        {
            _isEnqueueBlocked = false;
        }

        public void Clear()
        {
            TrackId = (ushort)(TrackId == ushort.MaxValue ? 1 : TrackId + 1);

            for (var i = 0; i < _currentCount; i++)
                globalQueue[offset + i] = Null;

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
                    throw new ArgumentException("Target array to small");

                array[arrayIndex + i] = this[i];
            }
        }

        public ArraySegment<byte> this[int index]
        {
            get => globalQueue[offset + _innerOffset + index];
            set => throw new NotSupportedException();
        }

        public IEnumerator<ArraySegment<byte>> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return globalQueue[offset + _innerOffset + i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        public void TrimSentBytes(int sentByteCount)
        {
            if (sentByteCount <= 0 || _currentCount <= _innerOffset)
                return;

            var accumlated = 0;

            for (var i = _innerOffset; i < _currentCount; i++)
            {
                var segment = globalQueue[offset + i];
                accumlated += segment.Count;

                if (accumlated <= sentByteCount)
                    continue;

                // Adjust the segment with remaining data
                var excess = accumlated - sentByteCount;
                _innerOffset = i;

                // Update the current segment with the leftover data
                if (segment.Array != null)
                {
                    globalQueue[offset + i] = new ArraySegment<byte>(
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

    public class SendingQueueSegmentCreator : IPoolSegmentFactory<SegmentQueue>
    {
        private readonly int _sendingQueueSize;

        public SendingQueueSegmentCreator(int sendingQueueSize)
        {
            if (sendingQueueSize <= 0)
                throw new ArgumentException("Sending queue size must be greater than zero.", nameof(sendingQueueSize));

            _sendingQueueSize = sendingQueueSize;
        }

        public IPoolSegment Create(int size, out SegmentQueue[] poolItems)
        {
            if (size <= 0)
                throw new ArgumentException("Size must be greater than zero.", nameof(size));

            var source = new ArraySegment<byte>[size * _sendingQueueSize];
            poolItems = new SegmentQueue[size];

            for (var i = 0; i < size; i++)
                poolItems[i] = new SegmentQueue(source, i * _sendingQueueSize, _sendingQueueSize);

            return new SendingPoolSegment(source, size);
        }
    }

    public class SendingPoolSegment(ArraySegment<byte>[] source, int count) : IPoolSegment
    {
        public ArraySegment<byte>[] Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
        public int Count { get; } = count;
    }
}
