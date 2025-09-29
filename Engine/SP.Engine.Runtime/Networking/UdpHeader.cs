using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpHeader : IHeader
    {
        private const int PeerIdSize = sizeof(ushort);
        private const int ProtocolIdSize = sizeof(ushort);
        private const int FlagsSize = sizeof(byte);
        private const int PayloadLengthSize = sizeof(int);

        private const int PeerIdOffset = 0;
        private const int ProtocolIdOffset = PeerIdOffset + PeerIdSize;
        private const int FlagsOffset = ProtocolIdOffset + ProtocolIdSize;
        private const int PayloadLengthOffset = FlagsOffset + FlagsSize;
        
        public const int HeaderSize = PeerIdSize + ProtocolIdSize + FlagsSize + PayloadLengthSize;

        public long SequenceNumber { get; }
        public PeerId PeerId { get; }
        public ushort Id  { get; }
        public HeaderFlags Flags { get; }
        public int PayloadLength { get; }
        public int Length => HeaderSize;
        public bool IsFragmentation => Flags.HasFlag(HeaderFlags.Fragment);

        public UdpHeader(PeerId peerId, ushort id, HeaderFlags flags, int payloadLength)
        {
            SequenceNumber = 0; // 사용안함
            PeerId = peerId;
            Id = id;
            Flags = flags;
            PayloadLength = payloadLength;
        }

        public void WriteTo(Span<byte> span)
        {
            span.WriteUInt16(PeerIdOffset, (ushort)PeerId);
            span.WriteUInt16(ProtocolIdOffset, Id);
            span[FlagsOffset] = (byte)Flags;
            span.WriteInt32(PayloadLengthOffset, PayloadLength);
        }

        public override string ToString()
        {
            return $"peerId={PeerId}, id={Id}, flags={Flags}, payloadLength={PayloadLength}";
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out UdpHeader header)
        {
            header = default;
            
            if (span.Length < HeaderSize)
                return false;
            
            header = new UdpHeader(
                (PeerId)span.ReadUInt16(PeerIdOffset),
                span.ReadUInt16(ProtocolIdOffset), 
                (HeaderFlags)span[FlagsOffset],
                span.ReadInt32(PayloadLengthOffset));
            return true;
        }
    }

    public class UdpHeaderBuilder
    {
        private PeerId _peerId;
        private ushort _id;
        private HeaderFlags _flags;
        private int _payloadLength;

        public UdpHeaderBuilder From(UdpHeader header)
        {
            _peerId = header.PeerId;
            _id = header.Id;
            _flags = header.Flags;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public UdpHeaderBuilder WithPeerId(PeerId peerId)
        {
            _peerId = peerId;
            return this;
        }

        public UdpHeaderBuilder WithId(ushort id)
        {
            _id = id;
            return this;
        }

        public UdpHeaderBuilder AddFlag(HeaderFlags flags)
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
            => new UdpHeader(_peerId, _id, _flags, _payloadLength);
    }
}
