using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Message
{
    public readonly struct TcpHeader
    {
        private const int SequenceNumberOffset = 0;
        private const int ProtocolIdOffset = SequenceNumberOffset + sizeof(long);
        private const int FlagsOffset = ProtocolIdOffset + sizeof(ushort);
        private const int PayloadLengthOffset = FlagsOffset + sizeof(byte);
        public const int HeaderSize = PayloadLengthOffset + sizeof(int);
        
        public long SequenceNumber { get; }
        public EProtocolId ProtocolId { get; }
        public EHeaderFlags Flags { get; }
        public int PayloadLength { get; }

        public TcpHeader(long sequenceNumber, EProtocolId protocolId, EHeaderFlags flags, int payloadLength)
        {
            SequenceNumber = sequenceNumber;
            ProtocolId = protocolId;
            Flags = flags;
            PayloadLength = payloadLength;
        }

        public void WriteTo(Span<byte> span)
        {
            span.WriteInt64(SequenceNumberOffset, SequenceNumber);
            span.WriteUInt16(ProtocolIdOffset, (ushort)ProtocolId);
            span[FlagsOffset] = (byte)Flags;
            span.WriteInt32(PayloadLengthOffset, PayloadLength);
        }
        
        public static bool TryParse(ReadOnlySpan<byte> span, out TcpHeader header)
        {
            header = default;
            
            if (span.Length < HeaderSize)
                return false;

            header = new TcpHeader(
                span.ReadInt64(SequenceNumberOffset),
                (EProtocolId)span.ReadUInt16(ProtocolIdOffset),
                (EHeaderFlags)span[FlagsOffset],
                span.ReadInt32(PayloadLengthOffset));
            return true;
        }
    }

    public class TcpHeaderBuilder
    {
        private long _sequenceNumber;
        private EProtocolId _protocolId;
        private EHeaderFlags _flags;
        private int _payloadLength;

        public TcpHeaderBuilder From(TcpHeader header)
        {
            _sequenceNumber = header.SequenceNumber;
            _protocolId = header.ProtocolId;
            _flags = header.Flags;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public TcpHeaderBuilder WithSequenceNumber(long sequenceNumber)
        {
            _sequenceNumber = sequenceNumber;
            return this;
        }

        public TcpHeaderBuilder WithProtocolId(EProtocolId protocolId)
        {
            _protocolId = protocolId;
            return this;
        }

        public TcpHeaderBuilder AddFlag(EHeaderFlags flags)
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
            => new TcpHeader(_sequenceNumber, _protocolId, _flags, _payloadLength);
    }
}
