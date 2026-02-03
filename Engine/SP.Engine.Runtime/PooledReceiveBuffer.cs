using System;
using System.Buffers;

namespace SP.Engine.Runtime
{
    public class PooledReceiveBuffer : IDisposable
    {
        private byte[] _buffer;
        private int _readHead;
        private int _writeHead;
        private int _count;
        private bool _disposed;

        public PooledReceiveBuffer(int initialCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }

        public int Capacity => _buffer.Length;
        public int ReadableBytes => _count;
        public int WriteableBytes => _buffer.Length - _count;

        public void Write(ReadOnlySpan<byte> data)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PooledReceiveBuffer));

            if (WriteableBytes < data.Length)
            {
                var newSize = Math.Max(_buffer.Length * 2, _count + data.Length);
                Resize(newSize);
            }
            
            var rightSpace = _buffer.Length - _writeHead;
            
            if (rightSpace >= data.Length)
            {
                data.CopyTo(_buffer.AsSpan(_writeHead));
                _writeHead += data.Length;
            }
            else
            {
                data[..rightSpace].CopyTo(_buffer.AsSpan(_writeHead));
                data[rightSpace..].CopyTo(_buffer.AsSpan(0));
                _writeHead = data.Length - rightSpace;
            }
            
            if (_writeHead == _buffer.Length) _writeHead = 0;
            _count += data.Length;
        }

        public void Peek(Span<byte> destination)
        {
            ThrowIfDisposed();
            
            if (ReadableBytes < destination.Length)
                throw new ArgumentOutOfRangeException(nameof(destination), "Not enough data to peek");

            var rightSpace = _buffer.Length - _readHead;
            if (rightSpace >= destination.Length)
            {
                _buffer.AsSpan(_readHead, destination.Length).CopyTo(destination);
            }
            else
            {
                var firstLen = rightSpace;
                var secondLen = destination.Length - rightSpace;
                
                _buffer.AsSpan(_readHead, firstLen).CopyTo(destination[..firstLen]);
                _buffer.AsSpan(0, secondLen).CopyTo(destination.Slice(firstLen, secondLen));
            }
        }
        
        public void Consume(int length)
        {
            ThrowIfDisposed();
            
            if (length > _count)
                throw new ArgumentOutOfRangeException(nameof(length), "Cannot consume more than available");
            
            _readHead = (_readHead + length) % _buffer.Length;
            _count -= length;
        }

        private void Resize(int newSize)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);

            if (_count > 0)
            {
                if (_readHead < _writeHead)
                {
                    _buffer.AsSpan(_readHead, _count).CopyTo(newBuffer);
                }
                else
                {
                    var rightSpace = _buffer.Length - _readHead;
                    _buffer.AsSpan(_readHead, rightSpace).CopyTo(newBuffer);
                    _buffer.AsSpan(0, _count - rightSpace).CopyTo(newBuffer.AsSpan(rightSpace));
                }
            }
            
            ArrayPool<byte>.Shared.Return(_buffer);
            
            _buffer = newBuffer;
            _readHead = 0;
            _writeHead = _count;
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
            
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PooledReceiveBuffer));
        }
    }
}
