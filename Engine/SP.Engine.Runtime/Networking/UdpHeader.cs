using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpHeader : IHeader
    {
        public const int ByteSize = 1 + 8 + 2 + 1 + 4; // 16 bytes

        public HeaderFlags Flags { get; }
        public long SessionId { get; }
        public ushort ProtocolId { get; }
        public byte Fragmented { get; }
        public int BodyLength { get; }
        
        public bool IsFragmented => Fragmented == 1;

        public UdpHeader(HeaderFlags flags, long sessionId, ushort protocolId, byte fragmented, int bodyLength)
        {
            Flags = flags;
            SessionId = sessionId;
            ProtocolId = protocolId;
            Fragmented = fragmented;
            BodyLength = bodyLength;
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
            destination.WriteInt32(12, BodyLength);
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
            var bodyLength = source.ReadInt32(12);
            header = new UdpHeader(flags, sessionId, protocolId, fragmented, bodyLength);
            byteConsumed = ByteSize;
            return true;
        }
    }

    public class UdpHeaderBuilder
    {
        private HeaderFlags _flags;
        private long _sessionId;
        private byte _fragmented;
        private ushort _protocolId;
        private int _bodyLength;

        public UdpHeaderBuilder From(UdpHeader header)
        {
            _flags = header.Flags;
            _sessionId = header.SessionId;
            _protocolId = header.ProtocolId;
            _fragmented = header.Fragmented;
            _bodyLength = header.BodyLength;
            return this;
        }

        public UdpHeaderBuilder WithSessionId(long sessionId)
        {
            _sessionId = sessionId;
            return this;
        }

        public UdpHeaderBuilder WithProtocolId(ushort protocolId)
        {
            _protocolId = protocolId;
            return this;
        }

        public UdpHeaderBuilder AddFlag(HeaderFlags flags)
        {
            _flags |= flags;
            return this;
        }

        public UdpHeaderBuilder WithFragmented(byte fragmented)
        {
            _fragmented = fragmented;
            return this;
        }

        public UdpHeaderBuilder WithBodyLength(int bodyLength)
        {
            _bodyLength = bodyLength;
            return this;
        }

        public UdpHeader Build()
        {
            return new UdpHeader(_flags, _sessionId, _protocolId, _fragmented, _bodyLength);
        }
    }
}
