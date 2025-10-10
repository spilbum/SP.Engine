using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpFragmentHeader
    {
        // id(4) + index(1} + totalCount(2) + payloadLen(2) = 9 bytes
        public const int ByteSize = 9;
        public uint Id { get; }
        public byte Index { get; }
        public ushort TotalCount { get; }
        public ushort PayloadLength { get; }
        public int Size { get; }

        public UdpFragmentHeader(uint id, byte index, ushort totalCount, ushort payloadLength)
        {
            Id = id;
            Index = index;
            TotalCount = totalCount;
            PayloadLength = payloadLength;
            Size = ByteSize;
        }
        
        public static bool TryParse(ReadOnlySpan<byte> source, out UdpFragmentHeader header, out int consumed)
        {
            header = default;
            consumed = 0;
            if (source.Length < ByteSize) return false;

            var id = source.ReadUInt32(0);
            var index = source[4];
            var totalCount = source.ReadUInt16(5);
            var payloadLength = source.ReadUInt16(7);
            header = new UdpFragmentHeader(id, index, totalCount, payloadLength);
            consumed = ByteSize;
            return true;
        }

        public void WriteTo(Span<byte> dst)
        {
            if (dst.Length < ByteSize) throw new ArgumentException("destination too small");
            dst.WriteUInt32(0, Id);
            dst[4] = Index;
            dst.WriteUInt16(5, TotalCount);
            dst.WriteUInt16(7, PayloadLength);
        }
    }
}
