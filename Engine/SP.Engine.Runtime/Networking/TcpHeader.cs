using System;
using System.Runtime.CompilerServices;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct TcpHeader : IHeader
    {
        private const int SequenceNumberSize = sizeof(long);
        private const int ProtocolIdSize = sizeof(ushort);
        private const int FlagsSize = sizeof(byte);
        private const int PayloadLengthSize = sizeof(int);
        
        private const int SequenceNumberOffset = 0;
        private const int ProtocolIdOffset = SequenceNumberOffset + SequenceNumberSize;
        private const int FlagsOffset = ProtocolIdOffset + ProtocolIdSize;
        private const int PayloadLengthOffset = FlagsOffset + FlagsSize;
        
        public const int HeaderSize = SequenceNumberSize + ProtocolIdSize + FlagsSize + PayloadLengthSize;
        
        public long SequenceNumber { get; }
        public EProtocolId ProtocolId { get; }
        public EHeaderFlags Flags { get; }
        public int PayloadLength { get; }
        public int Length => HeaderSize;

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

        public enum ParseResult
        {
            Success,
            Invalid,
            NeedMore,
        }
        
        public static ParseResult TryParse(ReadOnlySpan<byte> span, out TcpHeader header)
        {
            header = default;
            
            if (span.Length < HeaderSize)
                return ParseResult.NeedMore;

            var sequenceNumber = span.ReadInt64(SequenceNumberOffset);
            var protocolId = (EProtocolId)span.ReadUInt16(ProtocolIdOffset);
            var flags = (EHeaderFlags)span[FlagsOffset];
            var payloadLength = span.ReadInt32(PayloadLengthOffset);
            
            if (protocolId <= 0 || payloadLength < 0)
                return ParseResult.Invalid;
            
            header = new TcpHeader(sequenceNumber, protocolId, flags, payloadLength);
            return ParseResult.Success;
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
