using System;
using System.Buffers;

namespace SP.Engine.Runtime
{
    public class BinaryReceiveBuffer
    {
        private readonly byte[] _buffer;
        private int _readHead;
        private int _writeHead;
        private int _count;

        public BinaryReceiveBuffer(int capacity)
        {
            _buffer = new byte[capacity];
        }

        public int ReadableBytes => _count;
        public int WriteableBytes => _buffer.Length - _count;

        public void Write(byte[] src, int offset, int length)
        {
            if (WriteableBytes < length) throw new Exception("Buffer Overflow");

            var rightSpace = _buffer.Length - _writeHead;
            if (rightSpace >= length)
            {
                Array.Copy(src, offset, _buffer, _writeHead, length);
                _writeHead += length;
            }
            else
            {
                Array.Copy(src, offset, _buffer, _writeHead, rightSpace);
                Array.Copy(src, offset + rightSpace, _buffer, 0, length - rightSpace);
                _writeHead = length - rightSpace;
            }
            
            if (_writeHead == _buffer.Length) _writeHead = 0;
            
            _count += length;
        }

        public ReadOnlySpan<byte> Peek(int length)
        {
            if (ReadableBytes < length) throw new Exception("Not enough data");
            
            var rightSpace = _buffer.Length - _readHead;
            if (rightSpace >= length)
            {
                return _buffer.AsSpan(_readHead, length);
            }
            
            var temp = new byte[length];
            Array.Copy(_buffer, _readHead, temp, 0, rightSpace);
            Array.Copy(_buffer, 0, temp, rightSpace, length - rightSpace);
            return temp;
        }

        public void Consume(int length)
        {
            _readHead = (_readHead + length) % _buffer.Length;
            _count -= length;
        }

        public bool TryRead(int length, out ReadOnlyMemory<byte> payload, out IMemoryOwner<byte> leasedOwner)
        {
            payload = default;
            leasedOwner = null;
            
            if (ReadableBytes < length) return false;
            
            var rightSpace = _buffer.Length - _readHead;

            if (rightSpace >= length)
            {
                payload = new ReadOnlyMemory<byte>(_buffer, _readHead, length);
                return true;
            }

            var owner = MemoryPool<byte>.Shared.Rent(length);
            var tempSpan = owner.Memory.Span;

            new ReadOnlySpan<byte>(_buffer, _readHead, rightSpace).CopyTo(tempSpan);
            new ReadOnlySpan<byte>(_buffer, 0, length - rightSpace).CopyTo(tempSpan[rightSpace..]);

            payload = owner.Memory[..length];
            leasedOwner = owner;
            return true;
        }
    }
}
