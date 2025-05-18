using System;
using System.Buffers.Binary;

namespace SP.Engine.Runtime.Networking
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
            => BinaryPrimitives.ReadInt64LittleEndian(span.CheckedSlice(offset, sizeof(long)));

        public static ushort ReadUInt16(this ReadOnlySpan<byte> span, int offset)
            => BinaryPrimitives.ReadUInt16LittleEndian(span.CheckedSlice(offset, sizeof(ushort)));
        
        public static int ReadInt32(this ReadOnlySpan<byte> span, int offset)
            => BinaryPrimitives.ReadInt32LittleEndian(span.CheckedSlice(offset, sizeof(int)));
        
        public static void WriteInt64(this Span<byte> span, int offset, long value)
            => BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, sizeof(long)), value);
        public static void WriteUInt16(this Span<byte> span, int offset, ushort value) 
            => BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, sizeof(ushort)), value);
        
        public static void WriteInt32(this Span<byte> span, int offset, int value) 
            => BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, sizeof(int)), value);
    }
}
