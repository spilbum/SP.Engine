using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpFragmentHeader
    {
        public const int ByteSize = 8; // 4 + 1 + 1 + 2 = 8 bytes

        public uint FragId { get; }
        public byte Index { get; }
        public byte TotalCount { get; }
        public ushort FragLength { get; }

        public UdpFragmentHeader(uint fragId, byte index, byte totalCount, ushort fragLength)
        {
            FragId = fragId;
            Index = index;
            TotalCount = totalCount;
            FragLength = fragLength;
        }

        public static UdpFragmentHeader Read(ReadOnlySpan<byte> source)
        {
            if (source.Length < ByteSize) return default;

            var fragId = source.ReadUInt32(0);
            var index = source[4];
            var totalCount = source[5];
            var fragLength = source.ReadUInt16(6);
            return new UdpFragmentHeader(fragId, index, totalCount, fragLength);
        }

        public void WriteTo(Span<byte> dst)
        {
            if (dst.Length < ByteSize) throw new ArgumentException("destination too small");
            dst.WriteUInt32(0, FragId);
            dst[4] = Index;
            dst[5] = TotalCount;
            dst.WriteUInt16(6, FragLength);
        }
    }
}
