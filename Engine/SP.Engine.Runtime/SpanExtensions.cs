using System;
using System.Buffers.Binary;

namespace SP.Engine.Runtime
{
    public static class SpanExtensions
    {
        private static ReadOnlySpan<byte> CheckedSlice(this ReadOnlySpan<byte> span, int offset, int size)
        {
            if (span.Length < offset + size)
                throw new ArgumentOutOfRangeException(nameof(offset));
            return span.Slice(offset, size);
        }

        public static long ReadInt64(this ReadOnlySpan<byte> span, int offset)
            => BinaryPrimitives.ReadInt64BigEndian(span.CheckedSlice(offset, sizeof(long)));

        public static ushort ReadUInt16(this ReadOnlySpan<byte> span, int offset)
            => BinaryPrimitives.ReadUInt16BigEndian(span.CheckedSlice(offset, sizeof(ushort)));
        
        public static uint ReadUInt32(this ReadOnlySpan<byte> span, int offset)
            => BinaryPrimitives.ReadUInt32BigEndian(span.CheckedSlice(offset, sizeof(uint)));
        
        public static int ReadInt32(this ReadOnlySpan<byte> span, int offset)
            => BinaryPrimitives.ReadInt32BigEndian(span.CheckedSlice(offset, sizeof(int)));
        
        public static void WriteInt64(this Span<byte> span, int offset, long value)
            => BinaryPrimitives.WriteInt64BigEndian(span.Slice(offset, sizeof(long)), value);
        public static void WriteUInt16(this Span<byte> span, int offset, ushort value) 
            => BinaryPrimitives.WriteUInt16BigEndian(span.Slice(offset, sizeof(ushort)), value);
        
        public static void WriteInt32(this Span<byte> span, int offset, int value) 
            => BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset, sizeof(int)), value);
        
        public static void WriteUInt32(this Span<byte> span, int offset, uint value) 
            => BinaryPrimitives.WriteUInt32BigEndian(span.Slice(offset, sizeof(int)), value);
    }
}
