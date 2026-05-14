using System;
using System.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : MessageBase<UdpHeader>
    {
        public UdpMessage()
        {
        }
        
        public UdpMessage(UdpHeader header, IMemoryOwner<byte> bodyOwner, int bodyLength) 
            : base(header, bodyOwner, bodyLength)
        {
        }

        public int Size => UdpHeader.ByteSize + BodyLength;

        public void SetSessionId(long sessionId)
        {
            Header = new UdpHeaderBuilder()
                .From(Header)
                .WithSessionId(sessionId)
                .Build();
        }

        public void WriteTo(Span<byte> destination)
        {
            const int hSize = UdpHeader.ByteSize;

            var header = new UdpHeaderBuilder()
                .From(Header)
                .WithFragmented(0)
                .WithBodyLength(BodyLength)
                .Build();
            
            header.WriteTo(destination[..hSize]);

            if (BodyLength > 0)
            {
                BodySpan.CopyTo(destination.Slice(hSize, BodyLength));
            }
        }

        public void WriteFragmentTo(Span<byte> destination, 
            uint fragId, byte index, byte totalCount, int bodyOffset, ushort fragLen)
        {
            const int headerSize = UdpHeader.ByteSize;
            const int fragHeaderSize = UdpFragmentHeader.ByteSize;
            
            var normalizedHeader = new UdpHeaderBuilder()
                .From(Header)
                .WithFragmented(1)
                .WithBodyLength(fragHeaderSize + fragLen)
                .Build();
            
            normalizedHeader.WriteTo(destination[..headerSize]);

            var fragHeader = new UdpFragmentHeader(fragId, index, totalCount, fragLen);
            fragHeader.WriteTo(destination.Slice(headerSize, fragHeaderSize));
            
            BodySpan.Slice(bodyOffset, fragLen).CopyTo(destination.Slice(headerSize + fragHeaderSize, fragLen));
        }

        protected override UdpHeader CreateHeader(HeaderFlags flags, ushort protocolId, int bodyLength)
        {
            return new UdpHeaderBuilder()
                .From(Header)
                .WithProtocolId(protocolId)
                .WithBodyLength(bodyLength)
                .AddFlag(flags)
                .Build();
        }
    }
}
