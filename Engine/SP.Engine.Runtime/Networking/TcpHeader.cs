using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct TcpHeader : IHeader
    {
        public const int ByteSize = 1 + 4 + 2 + 4; // 11 bytes

        public int HeaderLength => ByteSize;
        public HeaderFlags Flags { get; }
        public uint SequenceNumber { get; }
        public ushort ProtocolId { get; }
        public int PayloadLength { get; }

        public TcpHeader(HeaderFlags flags, uint sequenceNumber, ushort protocolId, int payloadLength)
        {
            Flags = flags;
            SequenceNumber = sequenceNumber;
            ProtocolId = protocolId;
            PayloadLength = payloadLength;
        }

        public bool HasFlag(HeaderFlags flags)
        {
            return (Flags & flags) != 0;
        }

        public void WriteTo(Span<byte> destination)
        {
            destination[0] = (byte)Flags;
            destination.WriteUInt32(1, SequenceNumber);
            destination.WriteUInt16(5, ProtocolId);
            destination.WriteInt32(7, PayloadLength);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out TcpHeader header, out int byteConsumed)
        {
            header = default;
            byteConsumed = 0;
            
            if (source.Length < ByteSize) return false;
            
            var flags = (HeaderFlags)source[0];
            var sequenceNumber = source.ReadUInt32(1);
            var protocolId = source.ReadUInt16(5);
            var payloadLength = source.ReadInt32(7);
            header = new TcpHeader(flags, sequenceNumber, protocolId, payloadLength);
            byteConsumed = ByteSize;
            return true;
        }
    }

    public class TcpHeaderBuilder
    {
        private HeaderFlags _flags;
        private ushort _protocolId;
        private uint _sequenceNumber;
        private int _payloadLength;

        public TcpHeaderBuilder From(TcpHeader header)
        {
            _flags = header.Flags;
            _sequenceNumber = header.SequenceNumber;
            _protocolId = header.ProtocolId;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public TcpHeaderBuilder WithSequenceNumber(uint sequenceNumber)
        {
            _sequenceNumber = sequenceNumber;
            return this;
        }

        public TcpHeaderBuilder WithProtocolId(ushort protocolId)
        {
            _protocolId = protocolId;
            return this;
        }

        public TcpHeaderBuilder AddFlag(HeaderFlags flags)
        {
            _flags |= flags;
            return this;
        }

        public TcpHeaderBuilder WithPayloadLength(int payloadLength)
        {
            _payloadLength = payloadLength;
            return this;
        }

        public TcpHeader Build()
        {
            return new TcpHeader(_flags, _sequenceNumber, _protocolId, _payloadLength);
        }
    }
}
