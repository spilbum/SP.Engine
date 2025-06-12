using System;

namespace SP.Engine.Runtime.Message
{
    public readonly struct UdpFragmentHeader
    {
        private const int IdOffset = 0;
        private const int IndexOffset = IdOffset + sizeof(uint);
        private const int TotalCountOffset = IndexOffset + sizeof(byte);
        private const int PayloadLengthOffset = TotalCountOffset + sizeof(byte);
        public const int HeaderSize = PayloadLengthOffset + sizeof(ushort);
        
        public uint Id { get; }
        public byte Index { get; }
        public byte TotalCount { get; }
        public ushort PayloadLength { get; }
        
        public UdpFragmentHeader(uint id, byte index, byte totalCount, ushort payloadLength)
        {
            Id = id;
            Index = index;
            TotalCount = totalCount;
            PayloadLength = payloadLength;
        }

        public void WriteTo(Span<byte> span)
        {
            span.WriteUInt32(IdOffset, Id);
            span[IndexOffset] = Index;
            span[TotalCountOffset] = TotalCount;
            span.WriteUInt16(PayloadLengthOffset, PayloadLength);
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out UdpFragmentHeader udpFragmentHeader)
        {
            udpFragmentHeader = default;
            if (span.Length < HeaderSize)
                return false;
            
            udpFragmentHeader = new UdpFragmentHeader(
                span.ReadUInt32(IdOffset),
                span[IndexOffset],
                span[TotalCountOffset],
                span.ReadUInt16(PayloadLengthOffset));
            return true;
        }
    }
}
