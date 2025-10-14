using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpHeader : IHeader
    {
        public const int ByteSize = 1 + 4 + 2 + 1 + 4; // 12 bytes
        
        public HeaderFlags Flags { get; }
        public uint PeerId { get; }
        public ushort MsdId  { get; }
        public byte Fragmented { get; }
        public int PayloadLength { get; }
        public int Size { get; }

        public UdpHeader(HeaderFlags flags, uint peerId, ushort msdId, byte fragmented, int payloadLength)
        {            
            Flags = flags;
            PeerId = peerId;
            MsdId = msdId;
            Fragmented = fragmented;
            PayloadLength = payloadLength;
            Size = ByteSize;
        }

        public void WriteTo(Span<byte> destination)
        {
            destination[0] = (byte)Flags;
            destination.WriteUInt32(1, PeerId);
            destination.WriteUInt16(5, MsdId);
            destination[7] = Fragmented;
            destination.WriteInt32(8, PayloadLength);
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
            var peerId = source.ReadUInt32(1);
            var msgId = source.ReadUInt16(5);
            var fragmented = source[7];
            var payloadLength = source.ReadInt32(8);
            header = new UdpHeader(flags, peerId, msgId, fragmented, payloadLength);
            consumed = ByteSize;
            return true;
        }
    }

    public class UdpHeaderBuilder
    {
        private HeaderFlags _flags;
        private uint _peerId;
        private ushort _msgId;
        private byte _fragmented;
        private int _payloadLength;

        public UdpHeaderBuilder From(UdpHeader header)
        {
            _flags = header.Flags;
            _peerId = header.PeerId;
            _msgId = header.MsdId;
            _fragmented = header.Fragmented;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public UdpHeaderBuilder WithPeerId(uint peerId)
        {
            _peerId = peerId;
            return this;
        }

        public UdpHeaderBuilder WithId(ushort id)
        {
            _msgId = id;
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
            => new UdpHeader(_flags, _peerId, _msgId, _fragmented, _payloadLength);
    }
}
