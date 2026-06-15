using System;
using System.Buffers;
using System.Collections.Concurrent;

namespace SP.Core.Buffers
{
    public sealed class BufferResizer : IBufferResizer, IDisposable
    {
        private static readonly ConcurrentBag<BufferResizer> _pool = new ConcurrentBag<BufferResizer>();
        
        private byte[] _buffer;
        private bool _disposed;
        
        private BufferResizer() { }

        public static BufferResizer Rent(int initialCapacity = 256)
        {
            if (!_pool.TryTake(out var resizer))
            {
                resizer = new BufferResizer();
            }
            
            resizer._buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            resizer._disposed = false;
            return resizer;
        }

        public Span<byte> Span => _buffer;

        public Span<byte> Resize(int size, int position)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BufferResizer));
            
            if (size <= _buffer.Length) return _buffer;

            var newCapacity = Math.Max(_buffer.Length * 2, size);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);

            if (position > 0)
            {
                _buffer.AsSpan(0, position).CopyTo(newBuffer);
            }
            
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
            
            return _buffer;
        }

        public ReadOnlySpan<byte> GetWrittenSpan(int position)
            => _buffer.AsSpan(0, position);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
            
            _pool.Add(this);
        }
    }
}
