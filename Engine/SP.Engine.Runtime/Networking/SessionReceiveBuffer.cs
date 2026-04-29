using System;
using System.Buffers;
using System.IO;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public sealed class SessionReceiveBuffer : IDisposable
    {
        private readonly PooledBuffer _buffer;
        private readonly int _mask;
        private readonly int _capacity;
        private int _head;
        private int _tail;
        private int _available;
        private bool _disposed;
        private readonly object _lock = new object();

        public bool Disposed
        {
            get { lock (_lock) { return _disposed; } }
        }

        public int Available
        {
            get { lock (_lock) { return _available; } }
        }
        
        public int Capacity => _capacity;

        public SessionReceiveBuffer(int capacity)
        {
            var cap = 1;
            while (cap < capacity) cap <<= 1;
            _buffer = new PooledBuffer(cap);
            _capacity = cap;
            _mask = _capacity - 1;
        }

        public bool Write(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (_disposed || data.Length > _capacity - _available) return false;

                var distanceToEnd = _capacity - _tail;
                if (distanceToEnd >= data.Length)
                {
                    data.CopyTo(_buffer.Slice(_tail, data.Length));
                }
                else
                {
                    data[..distanceToEnd].CopyTo(_buffer.Slice(_tail, distanceToEnd));
                    data[distanceToEnd..].CopyTo(_buffer.Slice(0, data.Length - distanceToEnd));
                }
            
                _tail = (_tail + data.Length) & _mask;
                _available += data.Length;
                return true;
            }
        }

        public bool TryExtract(int maxFrameBytes, out TcpHeader header, out IMemoryOwner<byte> bodyOwner)
        {
            header = default;
            bodyOwner = null;
            const int headerSize = TcpHeader.ByteSize;

            lock (_lock)
            {
                if (_disposed || _available < headerSize) return false;
                
                Span<byte> headerSpan = stackalloc byte[headerSize];
                CopyTo(_head, headerSize, headerSpan);

                if (!TcpHeader.TryRead(headerSpan, out var tempHeader, out var consumed))
                    return false;

                var totalNeed = headerSize + tempHeader.BodyLength;
                if (_available < totalNeed) return false;

                if (tempHeader.BodyLength > maxFrameBytes)
                    throw new InvalidDataException($"Message body too large: {tempHeader.BodyLength}, max: {maxFrameBytes}");

                header = tempHeader;
                _head = (_head + consumed) & _mask;
                _available -= consumed;

                if (header.BodyLength > 0)
                {
                    var pooled = new PooledBuffer(header.BodyLength);
                    CopyTo(_head, header.BodyLength, pooled.Span);
                    
                    _head = (_head + header.BodyLength) & _mask;
                    _available -= header.BodyLength;
                    bodyOwner = pooled;
                }
                
                if (_available == 0) { _head = 0; _tail = 0; }
                return true;
            }
        }

        private void CopyTo(int head, int length, Span<byte> destination)
        {
            var distance = _capacity - head;
            if (distance >= length)
            {
                _buffer.Slice(head, length).CopyTo(destination);
            }
            else
            {
                _buffer.Slice(head, distance).CopyTo(destination[..distance]);
                _buffer.Slice(0, length - distance).CopyTo(destination[distance..]);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Dispose();
        }
    }
}
