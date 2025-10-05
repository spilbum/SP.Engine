using System;
using System.Collections.Generic;
using System.Threading;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : BaseMessage<UdpHeader>
    {
        public UdpMessage()
        {
            
        }
        
        public UdpMessage(UdpHeader header, ArraySegment<byte> payload)
            : base(header, payload)
        {
        }
        
        public void SetPeerId(uint peerId)
        {
            Header = new UdpHeaderBuilder()
                .From(Header)
                .WithPeerId(peerId)
                .Build();
        }

        public List<ArraySegment<byte>> ToDatagrams(int maxDatagramSize, Func<uint> getFragId)
        {
            var list = new List<ArraySegment<byte>>();
            var body = Body;
            var bodyLen = body.Count;

            if (maxDatagramSize <= 0 || UdpHeader.ByteSize + bodyLen <= maxDatagramSize)
            {
                var header = new UdpHeaderBuilder()
                    .From(Header)
                    .WithPayloadLength(bodyLen)
                    .Build();
                
                list.Add(ToArraySegment(header, body));
                return list;
            }

            var maxChunk = maxDatagramSize - UdpHeader.ByteSize - UdpFragmentHeader.ByteSize;
            if (maxChunk <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxChunk), "Too small for headers.");

            var totalCount = (byte)((bodyLen + maxChunk - 1) / maxChunk);
            var fragId = getFragId();
            
            var offset = 0;
            byte index = 0;

            while (offset < bodyLen)
            {
                var size = Math.Min(maxChunk, bodyLen - offset);
                var slice = new ArraySegment<byte>(body.Array!, body.Offset + offset, size);
                
                var fragHeader = new UdpFragmentHeader(fragId, index, totalCount, size);
                var length = UdpFragmentHeader.ByteSize + size;
                
                var header = new UdpHeaderBuilder()
                    .From(Header)
                    .AddFlag(HeaderFlags.Fragment)
                    .WithPayloadLength(length)
                    .Build();
              
                list.Add( ToArraySegment(header, fragHeader, slice));
                
                offset += size;
                index++;
            }
            
            return list;
        }

        private static ArraySegment<byte> ToArraySegment(UdpHeader header, ArraySegment<byte> payload)
        {
            var total = UdpHeader.ByteSize + header.PayloadLength;
            var buf = new byte[total];
            var span = buf.AsSpan();
            
            header.WriteTo(span[..UdpHeader.ByteSize]);

            if (payload.Count > 0 && payload.Array is { } arr)
                Buffer.BlockCopy(arr, payload.Offset, buf, UdpHeader.ByteSize, payload.Count);
            
            return new ArraySegment<byte>(buf);
        }

        private static ArraySegment<byte> ToArraySegment(UdpHeader header, UdpFragmentHeader fragHeader,
            ArraySegment<byte> fragPayload)
        {
            var total = UdpHeader.ByteSize + header.PayloadLength;
            var buf = new byte[total];
            var span = buf.AsSpan();
            
            var offset = 0;
            header.WriteTo(span.Slice(offset, UdpHeader.ByteSize));
            offset += UdpHeader.ByteSize;
            
            fragHeader.WriteTo(span.Slice(offset, UdpFragmentHeader.ByteSize));
            offset += UdpFragmentHeader.ByteSize;
            
            if (fragPayload.Count > 0 && fragPayload.Array is { } arr)
                Buffer.BlockCopy(arr, fragPayload.Offset, buf, offset, fragPayload.Count);
            
            return new ArraySegment<byte>(buf);
        }
        
        protected override UdpHeader CreateHeader(HeaderFlags flags, ushort id, int payloadLength)
        {
            return new UdpHeaderBuilder()
                .From(Header)
                .WithId(id)
                .WithPayloadLength(payloadLength)
                .AddFlag(flags)
                .Build();
        }
    }
}

