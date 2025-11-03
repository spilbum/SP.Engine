using System;
using System.Buffers;

namespace SP.Engine.Runtime
{
    public enum BufferPolicy
    {
        Fixed = 0,
        AutoGrow,
        CompactOnly
    }

    public class BinaryReceiveBuffer : IDisposable
    {
        private readonly int _growFactorPercent;
        private readonly int _maxBufferSize;
        private readonly BufferPolicy _policy;
        private bool _disposed;
        private Memory<byte> _memory;
        private IMemoryOwner<byte> _owner;
        private int _readIndex;
        private int _writeIndex;

        public BinaryReceiveBuffer(
            int capacity,
            BufferPolicy policy = BufferPolicy.Fixed,
            int? maxBufferSize = null,
            int growFactorPercent = 100)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (growFactorPercent <= 0) throw new ArgumentOutOfRangeException(nameof(growFactorPercent));

            _policy = policy;
            _maxBufferSize = maxBufferSize ?? 1024 * 1024 * 1024;
            _growFactorPercent = growFactorPercent;

            _owner = MemoryPool<byte>.Shared.Rent(capacity);
            _memory = _owner.Memory;
        }

        public int Capacity => _memory.Length;
        public int ReadableBytes => _writeIndex - _readIndex;
        public int WritableBytes => Capacity - _writeIndex;

        public ReadOnlySpan<byte> ReadableSpan => _memory.Span.Slice(_readIndex, ReadableBytes);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _memory = default;
            _owner.Dispose();
        }

        public void Consume(int count)
        {
            if (count < 0 || count > ReadableBytes)
                throw new ArgumentOutOfRangeException(nameof(count));
            _readIndex += count;
        }

        public void ResetIfConsumed()
        {
            if (ReadableBytes == 0)
                _readIndex = _writeIndex = 0;
        }

        public void CompactIfNeeded(int sizeHint)
        {
            if (_readIndex <= 0 || WritableBytes >= sizeHint) return;
            var readable = ReadableBytes;
            _memory.Span.Slice(_readIndex, readable).CopyTo(_memory.Span);
            _writeIndex = readable;
            _readIndex = 0;
        }

        public bool TryWrite(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length > buffer.Length - offset) throw new ArgumentOutOfRangeException(nameof(length));

            if (!EnsureWritable(length, false))
                return false;

            buffer.AsSpan(offset, length).CopyTo(_memory.Span.Slice(_writeIndex, length));
            _writeIndex += length;
            return true;
        }

        private bool EnsureWritable(int sizeHint, bool throwOnFail)
        {
            if (sizeHint < 0)
            {
                if (throwOnFail) throw new ArgumentOutOfRangeException(nameof(sizeHint));
                return false;
            }

            if (WritableBytes >= sizeHint) return true;

            var readable = ReadableBytes;

            if (_policy != BufferPolicy.Fixed && _readIndex > 0)
            {
                _memory.Span.Slice(_readIndex, readable).CopyTo(_memory.Span);
                _writeIndex = readable;
                _readIndex = 0;
                if (WritableBytes >= sizeHint) return true;
            }

            switch (_policy)
            {
                case BufferPolicy.AutoGrow:
                {
                    var required = (long)_writeIndex + sizeHint;
                    if (required > _maxBufferSize) return Fail();

                    var growBy = (int)Math.Max(_memory.Length * (long)_growFactorPercent / 100, sizeHint);
                    var newSize = (int)Math.Min(Math.Max(_memory.Length + growBy, required), _maxBufferSize);

                    var newOwner = MemoryPool<byte>.Shared.Rent(newSize);
                    _memory.Span[..readable].CopyTo(newOwner.Memory.Span);

                    _owner.Dispose();
                    _owner = newOwner;
                    _memory = newOwner.Memory;
                    _writeIndex = readable;
                    _readIndex = 0;
                    return true;
                }
                case BufferPolicy.Fixed:
                case BufferPolicy.CompactOnly:
                default:
                    return Fail();
            }

            bool Fail()
            {
                if (throwOnFail) throw new OutOfMemoryException($"Need {sizeHint} writable bytes (policy={_policy}).");
                return false;
            }
        }

        public bool TryReadMemory(int length, out ReadOnlyMemory<byte> memory)
        {
            if (length < 0 || ReadableBytes < length)
            {
                memory = default;
                return false;
            }

            memory = _memory.Slice(_readIndex, length);
            _readIndex += length;
            return true;
        }

        public bool TryReadBytes(int length, out byte[] bytes)
        {
            bytes = null;
            if (length < 0 || ReadableBytes < length) return false;
            var arr = new byte[length];
            _memory.Span.Slice(_readIndex, length).CopyTo(arr);
            _readIndex += length;
            bytes = arr;
            return true;
        }
    }
}
