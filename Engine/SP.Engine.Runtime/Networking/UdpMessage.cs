using System;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : BaseMessage<UdpHeader>
    {
        public int FrameLength => Header.Size + Body.Count + 1 /* 조각화 플래그 */;
        
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

        public List<ArraySegment<byte>> Split(uint fragId, ushort maxFragBodyLen)
        {
            if (maxFragBodyLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxFragBodyLen));
            
            var headerSize = Header.Size;
            const int fragHeaderSize = FragmentHeader.ByteSize;

            var bodyLen = Body.Count;
            var totalCount = (int)Math.Ceiling((double)bodyLen / maxFragBodyLen);

            var result = new List<ArraySegment<byte>>(totalCount);
            var src = Body.Array!;
            var srcOffset = Body.Offset;

            // 조각화
            for (byte index = 0; index < totalCount; index++)
            {
                var remaining = bodyLen - index * maxFragBodyLen;
                var fragLen = (ushort)Math.Min(remaining, maxFragBodyLen);

                var frameSize = headerSize + fragHeaderSize + fragLen + 1;
                var buf = new byte[frameSize];
                var offset = 0;
                
                // Udp 헤더
                var header = new UdpHeaderBuilder()
                    .From(Header)
                    .WithFragmented(1)
                    .WithPayloadLength(fragHeaderSize + fragLen)
                    .Build();
                
                header.WriteTo(buf.AsSpan(0, headerSize));
                offset += headerSize;

                // Fragment 헤더
                var fh = new FragmentHeader(fragId, index, (ushort)totalCount, fragLen);
                fh.WriteTo(buf.AsSpan(offset, fragHeaderSize));
                offset += fragHeaderSize;
                
                // 페이로드
                Buffer.BlockCopy(src, srcOffset + index * maxFragBodyLen, buf, offset, fragLen);
               
                result.Add(new ArraySegment<byte>(buf, 0, frameSize));
            }
            
            return result;
        }

        public ArraySegment<byte> ToArraySegment()
        {
            var body = Body;
            var header = new UdpHeaderBuilder()
                .From(Header)
                .WithFragmented(0)
                .WithPayloadLength((ushort)body.Count)
                .Build();
            
            var buf = new byte[FrameLength];
            var span = buf.AsSpan();
            
            // Udp 헤더
            var offset = 0;
            header.WriteTo(span[..header.Size]);
            offset += header.Size;
            
            // 페이로드
            if (body.Count > 0 && body.Array != null)
                Buffer.BlockCopy(body.Array, body.Offset, buf, offset, body.Count);
            
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

