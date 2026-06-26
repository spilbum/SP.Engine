using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct FragmentHeader
    {
        public const int ByteSize = 6; // 4 + 1 + 1 = 6 bytes

        public uint FragId { get; }
        public byte Index { get; }
        public byte TotalCount { get; }

        public FragmentHeader(uint fragId, byte index, byte totalCount)
        {
            FragId = fragId;
            Index = index;
            TotalCount = totalCount;
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out FragmentHeader header, out int bytesConsumed)
        {
            header = default;
            bytesConsumed = 0;

            if (source.Length < ByteSize) return false;

            var fragId = source.ReadUInt32(0);
            var index = source[4];
            var totalCount = source[5];
            header = new FragmentHeader(fragId, index, totalCount);
            bytesConsumed = ByteSize;
            return true;
        }

        public void WriteTo(Span<byte> destination)
        {
            if (destination.Length < ByteSize) throw new ArgumentException("destination too small");
            destination.WriteUInt32(0, FragId);
            destination[4] = Index;
            destination[5] = TotalCount;
        }
    }
}
