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

        public static void WriteSessionId(this Span<byte> span, int offset, string sessionId)
        {
            var guid = Guid.Parse(sessionId);
            guid.ToByteArray().CopyTo(span.Slice(offset, 16));
        }

        public static string ReadSessionId(this ReadOnlySpan<byte> span, int offset)
            => new Guid(span.Slice(offset, 16)).ToString();
        
        public static Guid ReadGuid(this ReadOnlySpan<byte> span, int offset)
        {
            return new Guid(span.CheckedSlice(offset, 16));
        }

        public static void WriteGuid(this Span<byte> span, int offset, Guid value)
        {
            var bytes = value.ToByteArray();
            if (span.Length < offset + 16)
                throw new ArgumentOutOfRangeException(nameof(offset));
            bytes.CopyTo(span.Slice(offset, 16));
        }
    }
}
