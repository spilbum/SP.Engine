using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SP.Core.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public enum MessageReadResult
    {
        Success,
        NeedMoreData,
        CorruptedPayload,
        InvalidHeader
    }
    
    public sealed class MessageReadBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly int _capacity;
        private readonly int _mask;
        private int _head;
        private int _tail;
        private int _size;
        private readonly object _lock = new object();

        public MessageReadBuffer(int capacity)
        {
            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _capacity = cap;
            _mask = cap - 1;
            _buffer = new byte[cap];
            _offset = 0;
        }

        public MessageReadBuffer(byte[] globalBuffer, int offset, int capacity)
        {
            _buffer = globalBuffer ?? throw new ArgumentNullException(nameof(globalBuffer));
            _offset = offset;

            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _capacity = cap;
            _mask = cap - 1;
        }

        public bool TryWrite(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (data.IsEmpty) return false;
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
        }
        
        public MessageReadResult TryRead(
            int maxPayloadLength, 
            out TcpHeader header,
            out IMemoryOwner<byte> bufferOwner)
        {
            header = default;
            bufferOwner = null;
            const int headerSize = TcpHeader.ByteSize;

            lock (_lock)
            {
                if (_size < headerSize) 
                    return MessageReadResult.NeedMoreData;
                
                Span<byte> headerSpan = stackalloc byte[headerSize];
                CopyTo(headerSize, headerSpan);

                if (!TcpHeader.TryRead(headerSpan, out var tempHeader, out var headerConsumed)) 
                    return MessageReadResult.InvalidHeader;
                
                var payloadLength = tempHeader.PayloadLength;
                var totalLength = headerConsumed + payloadLength;

                if (payloadLength < 0 || payloadLength > maxPayloadLength)
                {
                    return MessageReadResult.CorruptedPayload;
                }
                
                if (_size < totalLength) 
                    return MessageReadResult.NeedMoreData;
                
                header = tempHeader;

                var buffer = BufferOwnerPool.Rent(totalLength);
                CopyTo(totalLength, buffer.Memory.Span);
                
                bufferOwner = buffer;
                
                _head = (_head + totalLength) & _mask;
                _size -= totalLength;
                
                if (_size == 0)
                {
                    _head = _tail = 0;
                }

                return MessageReadResult.Success;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyTo(int length, Span<byte> destination)
        {
            var distanceToEnd = _capacity - _head;
            
            if (distanceToEnd >= length)
            {
                _buffer.AsSpan(_offset + _head, length).CopyTo(destination);
            }
            else
            {
                _buffer.AsSpan(_offset + _head, distanceToEnd).CopyTo(destination[..distanceToEnd]);
                _buffer.AsSpan(_offset, length - distanceToEnd).CopyTo(destination[distanceToEnd..]);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _head = _tail = _size = 0;   
            }
        }
    }
}
