using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpFragmentHeader
    {
        // id(4) + index(2} + totalCount(2) + payloadLen(2) = 10 bytes
        public const int ByteSize = 10;
        public uint Id { get; }
        public ushort Index { get; }
        public ushort TotalCount { get; }
        public ushort FragmentLength { get; }
        public int Size { get; }

        public UdpFragmentHeader(uint id, ushort index, ushort totalCount, ushort fragmentLength)
        {
            Id = id;
            Index = index;
            TotalCount = totalCount;
            FragmentLength = fragmentLength;
            Size = ByteSize;
        }
        
        public static bool TryParse(ReadOnlySpan<byte> source, out UdpFragmentHeader header, out int consumed)
        {
            header = default;
            consumed = 0;
            if (source.Length < ByteSize) return false;

            var id = source.ReadUInt32(0);
            var index = source.ReadUInt16(4);
            var totalCount = source.ReadUInt16(6);
            var payloadLength = source.ReadUInt16(8);
            header = new UdpFragmentHeader(id, index, totalCount, payloadLength);
            consumed = ByteSize;
            return true;
        }

        public void WriteTo(Span<byte> dst)
        {
            if (dst.Length < ByteSize) throw new ArgumentException("destination too small");
            dst.WriteUInt32(0, Id);
            dst.WriteUInt16(4, Index);
            dst.WriteUInt16(6, TotalCount);
            dst.WriteUInt16(8, FragmentLength);
        }
    }
}
