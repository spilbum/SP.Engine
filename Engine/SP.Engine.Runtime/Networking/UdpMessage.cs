using System;
using System.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : BaseMessage<UdpHeader>
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
            const int hSize = UdpHeader.ByteSize;
            const int fHeaderSize = UdpFragmentHeader.ByteSize;
            
            var nHeader = new UdpHeaderBuilder()
                .From(Header)
                .WithFragmented(1)
                .WithBodyLength(fHeaderSize + fragLen)
                .Build();
            
            nHeader.WriteTo(destination[..hSize]);

            var fh = new UdpFragmentHeader(fragId, index, totalCount, fragLen);
            fh.WriteTo(destination.Slice(hSize, fHeaderSize));
            
            BodySpan.Slice(bodyOffset, fragLen).CopyTo(destination.Slice(hSize + fHeaderSize, fragLen));
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
