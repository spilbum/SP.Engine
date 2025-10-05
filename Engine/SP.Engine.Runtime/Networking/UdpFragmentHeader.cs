using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpFragmentHeader
    {
        // id(4) + index(1} + totalCount(1) + payloadLen(4) = 10 bytes
        public const int ByteSize = 10;
        public uint Id { get; }
        public byte Index { get; }
        public byte TotalCount { get; }
        public int PayloadLength { get; }

        public UdpFragmentHeader(uint id, byte index, byte totalCount, int payloadLength)
        {
            Id = id;
            Index = index;
            TotalCount = totalCount;
            PayloadLength = payloadLength;
        }
        
        public static bool TryRead(ReadOnlySpan<byte> source, out UdpFragmentHeader header)
        {
            header = default;
            if (source.Length < ByteSize) return false;

            var id = source.ReadUInt32(0);
            var index = source[4];
            var totalCount = source[5];
            var payloadLength = source.ReadInt32(6);
            if (payloadLength < 0) return false;
            
            header = new UdpFragmentHeader(id, index, totalCount, payloadLength);
            return true;
        }

        public void WriteTo(Span<byte> destination)
        {
            if (destination.Length < ByteSize) throw new ArgumentException("destination too small");
            destination.WriteUInt32(0, Id);
            destination[4] = Index;
            destination[5] = TotalCount;
            destination.WriteInt32(6, PayloadLength);
        }
    }
}
