using System;
using System.Buffers;

namespace SP.Core
{
    public sealed class PooledBuffer : IDisposable
    {
        private byte[] _buffer;
        private readonly int _length;
        private bool _disposed;

        public int Length => _length;
        public int Capacity => _buffer?.Length ?? 0;

        public ReadOnlyMemory<byte> Memory => _disposed
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : new ReadOnlyMemory<byte>(_buffer, 0, _length);
        
        public Span<byte> Span => _disposed
            ? throw new ObjectDisposedException(nameof(PooledBuffer))
            : new Span<byte>(_buffer, 0, _length);

        public byte[] Array => _buffer;
        
        public PooledBuffer(int size)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(size);
            _length = size;
        }

        public static PooledBuffer Alloc(int size) => new PooledBuffer(size);

        public static PooledBuffer From(ReadOnlySpan<byte> data)
        {
            var pb = new PooledBuffer(data.Length);
            data.CopyTo(pb.Span);
            return pb;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            if (_buffer == null) return;
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }
}
