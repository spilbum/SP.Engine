using System;

namespace SP.Engine.Client
{
    public sealed class ByteRingBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private long _head;
        private long _tail;

        public ByteRingBuffer(int capacity)
        {
            _capacity = capacity;
            _buffer = new byte[_capacity];
        }
        
        public int GetPendingBytes() => (int)(_tail - _head);
        public int GetAvailableSpace() => _capacity - GetPendingBytes();

        public bool TryWrite(ReadOnlySpan<byte> source)
        {
            if (GetAvailableSpace() < source.Length)
                return false;

            var tailIndex = (int)(_tail % _capacity);
            var chunk = Math.Min(source.Length, _capacity - tailIndex);

            source.Slice(0, chunk).CopyTo(_buffer.AsSpan(tailIndex, chunk));
            if (source.Length > chunk)
            {
                source.Slice(chunk).CopyTo(_buffer.AsSpan(0, source.Length - chunk));
            }

            _tail += source.Length;
            return true;
        }

        public int GetContiguousReadBlock(out int offset)
        {
            var bytes = GetPendingBytes();
            if (bytes == 0)
            {
                offset = 0;
                return 0;
            }
            
            var headIndex = (int)(_head % _capacity);

            if (headIndex + bytes > _capacity)
            {
                bytes = _capacity - headIndex;
            }

            offset = headIndex;
            return bytes;
        }

        public void Consume(int bytesTransferred)
        {
            _head += bytesTransferred;
            if (_head == _tail)
            {
                _head = _tail = 0;
            }
        }
        
        public byte[] GetRawBuffer() => _buffer;

        public void Clear()
        {
            _head = 0;
            _tail = 0;
        }
    }
}
