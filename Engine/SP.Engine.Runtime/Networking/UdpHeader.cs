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
        public int PayloadLength { get; }
        public int Size { get; }

        public UdpHeader(HeaderFlags flags, long sessionId, ushort protocolId, byte fragmented, int payloadLength)
        {
            Flags = flags;
            SessionId = sessionId;
            ProtocolId = protocolId;
            Fragmented = fragmented;
            PayloadLength = payloadLength;
            Size = ByteSize;
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

        public static bool TryRead(ReadOnlySpan<byte> source, out UdpHeader header, out int consumed)
        {
            if (source.Length < ByteSize)
            {
                header = default;
                consumed = 0;
                return false;
            }

            var flags = (HeaderFlags)source[0];
            var sessionId = source.ReadInt64(1);
            var msgId = source.ReadUInt16(9);
            var fragmented = source[11];
            var payloadLength = source.ReadInt32(12);
            header = new UdpHeader(flags, sessionId, msgId, fragmented, payloadLength);
            consumed = ByteSize;
            return true;
        }
    }

    public class UdpHeaderBuilder
    {
        private HeaderFlags _flags;
        private long _sessionId;
        private byte _fragmented;
        private ushort _msgId;
        private int _payloadLength;

        public UdpHeaderBuilder From(UdpHeader header)
        {
            _flags = header.Flags;
            _sessionId = header.SessionId;
            _msgId = header.ProtocolId;
            _fragmented = header.Fragmented;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public UdpHeaderBuilder WithSessionId(long sessionId)
        {
            _sessionId = sessionId;
            return this;
        }

        public UdpHeaderBuilder WithMsgId(ushort msgId)
        {
            _msgId = msgId;
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

        public UdpHeaderBuilder WithPayloadLength(int payloadLength)
        {
            _payloadLength = payloadLength;
            return this;
        }

        public UdpHeader Build()
        {
            return new UdpHeader(_flags, _sessionId, _msgId, _fragmented, _payloadLength);
        }
    }
}
