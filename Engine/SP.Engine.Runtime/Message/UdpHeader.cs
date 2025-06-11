using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Message
{
    public readonly struct UdpHeader
    {
        private const int SequenceNumberOffset = 0;
        private const int PeerIdOffset = SequenceNumberOffset + sizeof(long);
        private const int ProtocolIdOffset = PeerIdOffset + sizeof(ushort);
        private const int FlagsOffset = ProtocolIdOffset + sizeof(ushort);
        private const int LengthOffset = FlagsOffset + sizeof(byte);
        public const int HeaderSize = LengthOffset + sizeof(int);

        public long SequenceNumber { get; }
        public EPeerId PeerId { get; }
        public EProtocolId ProtocolId  { get; }
        public EHeaderFlags Flags { get; }
        public int Length { get; }
        public bool IsFragmentation => Flags.HasFlag(EHeaderFlags.Fragmentation);

        public UdpHeader(long sequenceNumber, EPeerId peerId, EProtocolId protocolId, EHeaderFlags flags, int length)
        {
            SequenceNumber = sequenceNumber;
            PeerId = peerId;
            ProtocolId = protocolId;
            Flags = flags;
            Length = length;
        }

        public void WriteTo(Span<byte> span)
        {
            span.WriteInt64(SequenceNumberOffset, SequenceNumber);
            span.WriteUInt16(PeerIdOffset, (ushort)PeerId);
            span.WriteUInt16(ProtocolIdOffset, (ushort)ProtocolId);
            span[FlagsOffset] = (byte)Flags;
            span.WriteInt32(LengthOffset, Length);
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out UdpHeader header)
        {
            header = default;
            
            if (span.Length < HeaderSize)
                return false;

            header = new UdpHeader(
                span.ReadInt64(SequenceNumberOffset),
                (EPeerId)span.ReadUInt16(PeerIdOffset),
                (EProtocolId)span.ReadUInt16(ProtocolIdOffset), 
                (EHeaderFlags)span[FlagsOffset],
                span.ReadInt32(LengthOffset));
            return true;
        }
    }

    public class UdpHeaderBuilder
    {
        private long _sequenceNumber;
        private EPeerId _peerId;
        private EProtocolId _protocolId;
        private EHeaderFlags _flags;
        private int _payloadLength;

        public UdpHeaderBuilder From(UdpHeader header)
        {
            _sequenceNumber = header.SequenceNumber;
            _peerId = header.PeerId;
            _protocolId = header.ProtocolId;
            _flags = header.Flags;
            _payloadLength = header.Length;
            return this;
        }

        public UdpHeaderBuilder WithSequenceNumber(long sequenceNumber)
        {
            _sequenceNumber = sequenceNumber;
            return this;
        }

        public UdpHeaderBuilder WithPeerId(EPeerId peerId)
        {
            _peerId = peerId;
            return this;
        }

        public UdpHeaderBuilder WithProtocolId(EProtocolId protocolId)
        {
            _protocolId = protocolId;
            return this;
        }

        public UdpHeaderBuilder AddFlag(EHeaderFlags flags)
        {
            _flags |= flags;
            return this;
        }
        
        public UdpHeaderBuilder WithPayloadLength(int payloadLength)
        {
            _payloadLength = payloadLength;
            return this;
        }

        public UdpHeader Build()
            => new UdpHeader(_sequenceNumber, _peerId, _protocolId, _flags, _payloadLength);
    }
}
