using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SP.Core.Buffers
{
    public sealed class PooledBuffer : IMemoryOwner<byte>
    {
        private const int DefaultCapacity = 1024;
        private byte[] _buffer;
        private int _disposed;

        public Memory<byte> Memory => _buffer.AsMemory();
        public int Length => _buffer.Length;
        
        public PooledBuffer(int capacity = DefaultCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(capacity);
            BufferMetrics.OnRent();
        }
        
        public byte[] GetRawBuffer() => _buffer;

        public int ExpandRingBuffer(int newCapacity, int head, int tail, int available)
        {
            ThrowIfDisposed();

            if (newCapacity <= _buffer.Length)
                return tail;
            
            var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);

            try
            {
                if (available > 0)
                {
                    if (head < tail)
                    {
                        _buffer.AsSpan(head, available).CopyTo(newBuffer);
                    }
                    else
                    {
                        var distanceToEnd = _buffer.Length - head;
                        _buffer.AsSpan(head, distanceToEnd).CopyTo(newBuffer);
                        _buffer.AsSpan(0, tail).CopyTo(newBuffer.AsSpan(distanceToEnd));
                    }
                }
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                throw;
            }
            
            var oldBuffer = _buffer;
            _buffer = newBuffer;

            if (oldBuffer == null) 
                return available;
            
            ArrayPool<byte>.Shared.Return(oldBuffer);
            return available;
        }

        public Span<byte> Slice(int start, int length)
        {
            ThrowIfDisposed();
            return _buffer.AsSpan(start, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed != 0) ThrowObjectDisposedException();
        }
        
        [method: MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowObjectDisposedException() => throw new ObjectDisposedException(nameof(PooledBuffer));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            
            var buf = _buffer;
            _buffer = null;

            if (buf == null) return;
            ArrayPool<byte>.Shared.Return(buf);
            BufferMetrics.OnReturn();
        }
    }
}
