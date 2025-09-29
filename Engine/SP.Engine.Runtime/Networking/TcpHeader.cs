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
        public ushort Id { get; }
        public HeaderFlags Flags { get; }
        public int PayloadLength { get; }
        public int Length => HeaderSize;

        public TcpHeader(long sequenceNumber, ushort id, HeaderFlags flags, int payloadLength)
        {
            SequenceNumber = sequenceNumber;
            Id = id;
            Flags = flags;
            PayloadLength = payloadLength;
        }

        public void WriteTo(Span<byte> span)
        {
            span.WriteInt64(SequenceNumberOffset, SequenceNumber);
            span.WriteUInt16(ProtocolIdOffset, Id);
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
            var id = span.ReadUInt16(ProtocolIdOffset);
            var flags = (HeaderFlags)span[FlagsOffset];
            var payloadLength = span.ReadInt32(PayloadLengthOffset);
            
            if (id <= 0 || payloadLength < 0)
                return ParseResult.Invalid;
            
            header = new TcpHeader(sequenceNumber, id, flags, payloadLength);
            return ParseResult.Success;
        }
    }

    public class TcpHeaderBuilder
    {
        private long _sequenceNumber;
        private ushort _id;
        private HeaderFlags _flags;
        private int _payloadLength;

        public TcpHeaderBuilder From(TcpHeader header)
        {
            _sequenceNumber = header.SequenceNumber;
            _id = header.Id;
            _flags = header.Flags;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public TcpHeaderBuilder WithSequenceNumber(long sequenceNumber)
        {
            _sequenceNumber = sequenceNumber;
            return this;
        }

        public TcpHeaderBuilder WithId(ushort id)
        {
            _id = id;
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
            => new TcpHeader(_sequenceNumber, _id, _flags, _payloadLength);
    }
}
