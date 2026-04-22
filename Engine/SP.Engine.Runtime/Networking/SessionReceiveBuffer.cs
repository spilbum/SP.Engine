using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SP.Engine.Runtime.Networking
{
    public sealed class SessionReceiveBuffer : IDisposable
    {
        private byte[] _buffer;
        private readonly int _mask;
        private readonly int _capacity;
        private int _head;
        private int _tail;
        private int _available;
        private bool _disposed;
        private readonly object _lock = new object();

        public int Available
        {
            get
            {
                lock (_lock) return _available;
            }
        }

        public SessionReceiveBuffer(int capacity)
        {
            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _buffer = ArrayPool<byte>.Shared.Rent(cap);
            _capacity = cap;
            _mask = _capacity - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWrite(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (_disposed || data.Length > _capacity - _available) return false;

                var distanceToEnd = _capacity - _tail;
                if (distanceToEnd >= data.Length)
                {
                    data.CopyTo(_buffer.AsSpan(_tail));
                }
                else
                {
                    data[..distanceToEnd].CopyTo(_buffer.AsSpan(_tail));
                    data[distanceToEnd..].CopyTo(_buffer.AsSpan(0));
                }
            
                _tail = (_tail + data.Length) & _mask;
                _available += data.Length;
                return true;
            }
        }

        public bool TryPeek(Span<byte> destination, int length)
        {
            lock (_lock)
            {
                if (_disposed || _available < length) return false;
                
                var distanceToEnd = _capacity - _head;
                if (distanceToEnd >= length)
                {
                    _buffer.AsSpan(_head, length).CopyTo(destination);
                }
                else
                {
                    _buffer.AsSpan(_head, distanceToEnd).CopyTo(destination[..distanceToEnd]);
                    _buffer.AsSpan(0, length - distanceToEnd).CopyTo(destination[distanceToEnd..length]);
                }
                
                return true;
            }
        }

        public void Consume(int length)
        {
            lock (_lock)
            {
                if (_disposed || length > _available) return;
            
                _head = (_head + length) & _mask;
                _available -= length;

                if (_available != 0) return;
                
                _head = 0;
                _tail = 0;
            }
        }
        
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
            
                var buf = _buffer;
                _buffer = null;

                if (buf != null)
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            
                _disposed = true;
            }
        }
    }
}
