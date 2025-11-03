using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct FragmentHeader
    {
        public const int ByteSize = 8; // 4 + 1 + 1 + 2 = 8 bytes

        public uint FragId { get; }
        public byte Index { get; }
        public byte TotalCount { get; }
        public ushort FragLength { get; }

        public FragmentHeader(uint fragId, byte index, byte totalCount, ushort fragLength)
        {
            FragId = fragId;
            Index = index;
            TotalCount = totalCount;
            FragLength = fragLength;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out FragmentHeader header, out int consumed)
        {
            header = default;
            consumed = 0;
            if (source.Length < ByteSize) return false;

            var fragId = source.ReadUInt32(0);
            var index = source[4];
            var totalCount = source[5];
            var fragLength = source.ReadUInt16(6);
            header = new FragmentHeader(fragId, index, totalCount, fragLength);
            consumed = ByteSize;
            return true;
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
