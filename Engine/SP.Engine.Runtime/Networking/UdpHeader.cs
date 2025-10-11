using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpHeader : IHeader
    {
        public const int ByteSize = 1 + 4 + 2 + 4; // 11 bytes
        
        public HeaderFlags Flags { get; }
        public uint PeerId { get; }
        public ushort Id  { get; }
        public int PayloadLength { get; }
        public int Size { get; }

        public UdpHeader(HeaderFlags flags, uint peerId, ushort id, int payloadLength)
        {            
            Flags = flags;
            PeerId = peerId;
            Id = id;
            PayloadLength = payloadLength;
            Size = ByteSize;
        }

        public void WriteTo(Span<byte> s)
        {
            s[0] = (byte)Flags;
            s.WriteUInt32(1, PeerId);
            s.WriteUInt16(5, Id);
            s.WriteInt32(7, PayloadLength);
        }

        public static bool TryParse(ReadOnlySpan<byte> source, out UdpHeader header, out int consumed)
        {
            if (source.Length < ByteSize)
            {
                header = default;
                consumed = 0;
                return false;
            }
            
            var flags = (HeaderFlags)source[0];
            var peerId = source.ReadUInt32(1);
            var id = source.ReadUInt16(5);
            var length = source.ReadInt32(7);
            header = new UdpHeader(flags, peerId, id, length);
            consumed = ByteSize;
            return true;
        }
    }

    public class UdpHeaderBuilder
    {
        private HeaderFlags _flags;
        private uint _peerId;
        private ushort _id;
        private int _payloadLength;

        public UdpHeaderBuilder From(UdpHeader header)
        {
            _flags = header.Flags;
            _peerId = header.PeerId;
            _id = header.Id;
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
            => new UdpHeader(_flags, _peerId, _id, _payloadLength);
    }
}
