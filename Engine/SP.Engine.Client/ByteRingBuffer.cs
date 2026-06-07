using System;
using System.Threading;

namespace SP.Engine.Client
{
    public sealed class ByteRingBuffer
    {
        private readonly byte[] _buffer;
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
            if ((capacity & (capacity - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a power of 2.");
            }
            
            _capacity = capacity;
            _mask = capacity - 1;
            _buffer = new byte[capacity];
        }
        

        public BufferLockScope Lock()
        {
            Monitor.Enter(_sync);
            return new BufferLockScope(this);
        }

        private bool TryWrite(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return true;
            if (data.Length > _capacity - _size) return false;

            if (_tail + data.Length <= _capacity)
            {
                data.CopyTo(_buffer.AsSpan(_tail, data.Length));
                _tail = (_tail + data.Length) & _mask;
            }
            else
            {
                var fChunk = _capacity - _tail;
                var sChunk = data.Length - fChunk;
                
                data.Slice(0, fChunk).CopyTo(_buffer.AsSpan(_tail, fChunk));
                data.Slice(fChunk).CopyTo(_buffer.AsSpan(0, sChunk));

                _tail = sChunk;
            }
            
            _size += data.Length;
            return true;
        }

        private ArraySegment<byte> GetReadableSegment()
        {
            if (_size == 0) return ArraySegment<byte>.Empty;
            
            return _head < _tail 
                ? new ArraySegment<byte>(_buffer, _head, _tail - _head) 
                : new ArraySegment<byte>(_buffer, _head, _capacity - _head);
        }

        private void AdvanceRead(int bytesTransferred)
        {
            if (bytesTransferred <= 0) return;
            if (bytesTransferred > _size)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesTransferred), "Cannot advance read pointer beyond current size.");
            }
            
            _head = (_head + bytesTransferred) & _mask;
            _size -= bytesTransferred;
        }

        private void Clear()
        {
            _head = _tail = _size = 0;
        }

        public readonly struct BufferLockScope : IDisposable
        {
            private readonly ByteRingBuffer _buffer;
            public BufferLockScope(ByteRingBuffer buffer) => _buffer = buffer;

            public bool TryWrite(ReadOnlySpan<byte> data) => _buffer.TryWrite(data);
            public ArraySegment<byte> GetReadableSegment() => _buffer.GetReadableSegment();
            public void AdvanceRead(int bytesTransferred) => _buffer.AdvanceRead(bytesTransferred);
            public void Clear() => _buffer.Clear();

            public void Dispose()
            {
                if (_buffer != null) Monitor.Exit(_buffer._sync);
            }
        }
    }
}
