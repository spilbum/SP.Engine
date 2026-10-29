using System;
using System.Threading;

namespace SP.Core.Buffers
{
    public sealed class ByteRingBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly int _capacity;
        private readonly int _mask;
        
        private int _head;
        private int _tail;
        private int _size;
        
        private readonly object _sync = new object();
        
        public int Capacity => _capacity;

        public int Size
        {
            get
            {
                lock (_sync)
                {
                    return _size;
                }
            }
        }

        public ByteRingBuffer(int capacity)
        {
            _capacity = NextPowerOfTwo(capacity);
            _mask = _capacity - 1;
            _buffer = new byte[_capacity];
            _offset = 0;
            ClearInternal();
        }

        public ByteRingBuffer(byte[] globalBuffer, int offset, int capacity)
        {
            _capacity = NextPowerOfTwo(capacity);
            _buffer = globalBuffer ?? throw new ArgumentNullException(nameof(globalBuffer));
            _offset = offset;
            _mask = _capacity - 1;
            ClearInternal();
        }

        private static int NextPowerOfTwo(int x)
        {
            var cap = 1;
            while (cap < x) cap <<= 1;
            return cap;
        }

        public BufferLockScope Lock()
        {
            Monitor.Enter(_sync);
            return new BufferLockScope(this);
        }

        public bool TryWrite(ReadOnlySpan<byte> data)
        {
            lock (_sync) { return TryWriteInternal(data); }
        }

        public ArraySegment<byte> GetReadableSegment()
        {
            lock (_sync) { return GetReadableSegmentInternal(); }
        }

        public void AdvanceRead(int bytesTransferred)
        {
            lock (_sync) { AdvanceReadInternal(bytesTransferred); }
        }
        
        public void Clear()
        {
            lock (_sync) { ClearInternal(); }
        }

        private bool TryWriteInternal(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return true;
            if (data.Length > _capacity - _size) return false;

            var actualTail = _offset + _tail;
            if (_tail + data.Length <= _capacity)
            {
                data.CopyTo(_buffer.AsSpan(actualTail, data.Length));
                _tail = (_tail + data.Length) & _mask;
            }
            else
            {
                var firstChunk = _capacity - _tail;
                var secondChunk = data.Length - firstChunk;
                
                data[..firstChunk].CopyTo(_buffer.AsSpan(actualTail, firstChunk));
                data[firstChunk..].CopyTo(_buffer.AsSpan(_offset, secondChunk));

                _tail = secondChunk;
            }
            
            _size += data.Length;
            return true;
        }

        private ArraySegment<byte> GetReadableSegmentInternal()
        {
            if (_size == 0) return ArraySegment<byte>.Empty;
            
            var actualHead = _offset + _head;
            return _head < _tail 
                ? new ArraySegment<byte>(_buffer, actualHead, _tail - _head) 
                : new ArraySegment<byte>(_buffer, actualHead, _capacity - _head);
        }

        private void AdvanceReadInternal(int bytesTransferred)
        {
            if (bytesTransferred <= 0) return;
            if (bytesTransferred > _size)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesTransferred), "Cannot advance read pointer beyond current size.");
            }
            
            _head = (_head + bytesTransferred) & _mask;
            _size -= bytesTransferred;
        }

        private void ClearInternal()
        {
            _head = _tail = _size = 0;
        }

        public readonly struct BufferLockScope : IDisposable
        {
            private readonly ByteRingBuffer _buffer;
            
            public BufferLockScope(ByteRingBuffer buffer) => _buffer = buffer;

            public bool TryWrite(ReadOnlySpan<byte> data) => _buffer.TryWriteInternal(data);
            public ArraySegment<byte> GetReadableSegment() => _buffer.GetReadableSegmentInternal();
            public void AdvanceRead(int bytesTransferred) => _buffer.AdvanceReadInternal(bytesTransferred);
            public void Clear() => _buffer.ClearInternal();

            public void Dispose()
            {
                if (_buffer != null) Monitor.Exit(_buffer._sync);
            }
        }
    }
}
