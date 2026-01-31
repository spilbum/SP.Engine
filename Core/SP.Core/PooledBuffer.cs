using System;
using System.Buffers;

namespace SP.Core
{
    public struct PooledBuffer : IDisposable
    {
        private byte[] _buffer;
        private bool _disposed;

        public int Count { get; }
        
        public PooledBuffer(int size)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(size);
            Count = size;
            _disposed = false;
        }
        
        public byte[] Array => _disposed 
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : _buffer;
        
        public Span<byte> Span => _disposed
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : new Span<byte>(_buffer, 0, Count);

        public PooledBuffer Clone()
        {
            var clone = new PooledBuffer(Count);
            Span.CopyTo(clone.Span);
            return clone;
        }
        
        public void Dispose()
        {
            if (_disposed || _buffer == null) return;
            
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
            _disposed = true;
        }


    }
}
