using System;

namespace SP.Core.Buffers
{
    public sealed class BufferResizer : IBufferResizer, IDisposable
    {
        private BufferOwner _bufferOwner;
        private bool _disposed;

        public BufferResizer(int initialCapacity = 1024)
        {
            _bufferOwner = new BufferOwner(initialCapacity);
        }

        public Span<byte> Span => _bufferOwner.Memory.Span;

        public Span<byte> Resize(int size, int position)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BufferResizer));

            var newCapacity = _bufferOwner.Length * 2;
            if (newCapacity < size) newCapacity = size;
            
            var newBuffer = new BufferOwner(newCapacity);

            if (position > 0)
            {
                _bufferOwner[..position].CopyTo(newBuffer.Memory.Span);
            }
            
            _bufferOwner.Dispose();
            _bufferOwner = newBuffer;
            return newBuffer.Memory.Span;
        }

        public ReadOnlySpan<byte> GetWrittenSpan(int position)
            => _bufferOwner.Memory.Span[..position];

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _bufferOwner?.Dispose();
            _bufferOwner = null;
        }
    }
}
