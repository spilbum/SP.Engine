using System;
using System.Buffers;

namespace SP.Core
{
    public struct RentedBuffer : IDisposable
    {
        private byte[] _rented;
        public int Length { get; }

        public static RentedBuffer Empty => default;
        
        public RentedBuffer(int length)
        {
            _rented = ArrayPool<byte>.Shared.Rent(length);
            Length = length;
            BufferMetrics.OnRent();
        }

        public RentedBuffer(byte[] rented, int length)
        {
            _rented = rented;
            Length = length;
            BufferMetrics.OnRent();
        }

        public Span<byte> Span
            => _rented == null ? Span<byte>.Empty : _rented.AsSpan(0, Length);

        public ArraySegment<byte> AsSegment(int offset, int count)
            => _rented == null ? default : new ArraySegment<byte>(_rented, offset, count);

        public void Dispose()
        {
            var buf = _rented;
            _rented = null;
            
            if (buf != null)
            {
                ArrayPool<byte>.Shared.Return(buf);   
                BufferMetrics.OnReturn();
            }
        }
    }
}
