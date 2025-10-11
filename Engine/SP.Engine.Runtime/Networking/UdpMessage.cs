using System;
using System.Collections.Generic;

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

        public List<ArraySegment<byte>> ToDatagrams(ushort maxDatagramSize, Func<uint> getFragId)
        {
            if (getFragId == null) throw new ArgumentNullException(nameof(getFragId));
            
            var body = Body;
            if (body.Count > 0 && body.Array == null)
                throw new InvalidOperationException("Body has non-zero length but no underlying array.");
            
            if (maxDatagramSize <= Header.Size)
                throw new ArgumentOutOfRangeException(nameof(maxDatagramSize), "Too small: must be > UDP header size.");
            
            var list = new List<ArraySegment<byte>>();
            var bodyLen = body.Count;

            // 단편
            if (Header.Size + bodyLen <= maxDatagramSize)
            {
                var header = new UdpHeaderBuilder().From(Header).WithPayloadLength(bodyLen).Build();
                list.Add(ToArraySegment(header, body));
                return list;
            }

            // 조각화
            var maxChunk = maxDatagramSize - Header.Size - UdpFragmentHeader.ByteSize;
            if (maxChunk <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxChunk), "Too small for headers.");

            var total = (bodyLen + maxChunk - 1) / maxChunk;
            if (total > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxDatagramSize), $"Too many fragments: {total} (<{ushort.MaxValue}). Reduce payload or increase MTU.");

            var totalCount = (ushort)total;
            list = new List<ArraySegment<byte>>(totalCount);
            
            var fragId = getFragId();
            var offset = 0;
            ushort index = 0;

            while (offset < bodyLen)
            {
                var fragLen = (ushort)Math.Min(maxChunk, bodyLen - offset);
                var fragHeader = new UdpFragmentHeader(fragId, index, totalCount, fragLen);
                var fragPayload = new ArraySegment<byte>(body.Array!, body.Offset + offset, fragLen);
                
                var header = new UdpHeaderBuilder()
                    .From(Header)
                    .AddFlag(HeaderFlags.Fragment)
                    .WithPayloadLength(fragHeader.Size + fragLen)
                    .Build();
                    
                var segment = ToArraySegment(header, fragHeader, fragPayload);
                list.Add(segment);
      
                offset += fragLen;
                index++;
            }
            
            return list;
        }

        private static ArraySegment<byte> ToArraySegment(UdpHeader header, ArraySegment<byte> payload)
        {
            var total = header.Size + header.PayloadLength;
            var buf = new byte[total];
            var span = buf.AsSpan();
            
            header.WriteTo(span[..header.Size]);
            payload.AsSpan().CopyTo(span.Slice(header.Size, payload.Count));
            return new ArraySegment<byte>(buf);
        }

        private static ArraySegment<byte> ToArraySegment(
            UdpHeader header, 
            UdpFragmentHeader fragHeader,
            ArraySegment<byte> fragPayload)
        {
            var total = header.Size + header.PayloadLength;
            var buf = new byte[total];
            var span = buf.AsSpan();
            
            var offset = 0;
            header.WriteTo(span.Slice(offset, header.Size));
            offset += header.Size;
            
            fragHeader.WriteTo(span.Slice(offset, fragHeader.Size));
            offset += fragHeader.Size;
            
            fragPayload.AsSpan().CopyTo(span.Slice(offset, fragPayload.Count));
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

