using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpHeader : IHeader
    {
        public const int ByteSize = 1 + 8 + 2 + 1 + 4; // 16 bytes

        public int HeaderLength => ByteSize;
        public HeaderFlags Flags { get; }
        public long SessionId { get; }
        public ushort ProtocolId { get; }
        public byte Fragmented { get; }
        public int PayloadLength { get; }
        
        public bool IsFragmented => Fragmented == 1;

        public UdpHeader(HeaderFlags flags, long sessionId, ushort protocolId, byte fragmented, int payloadLength)
        {
            Flags = flags;
            SessionId = sessionId;
            ProtocolId = protocolId;
            Fragmented = fragmented;
            PayloadLength = payloadLength;
        }

        public bool HasFlag(HeaderFlags flags)
        {
            return (Flags & flags) != 0;
        }

        public void WriteTo(Span<byte> destination)
        {
            destination[0] = (byte)Flags;
            destination.WriteInt64(1, SessionId);
            destination.WriteUInt16(9, ProtocolId);
            destination[11] = Fragmented;
            destination.WriteInt32(12, PayloadLength);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out UdpHeader header, out int byteConsumed)
        {
            header = default;
            byteConsumed = 0;
            
            if (source.Length < ByteSize) return false;

            var flags = (HeaderFlags)source[0];
            var sessionId = source.ReadInt64(1);
            var protocolId = source.ReadUInt16(9);
            var fragmented = source[11];
            var payloadLength = source.ReadInt32(12);
            header = new UdpHeader(flags, sessionId, protocolId, fragmented, payloadLength);
            byteConsumed = ByteSize;
            return true;
        }
    }
}
