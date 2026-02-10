using System;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : BaseMessage<UdpHeader>
    {
        public UdpMessage()
        {
        }
        
        public UdpMessage(UdpHeader header, ReadOnlyMemory<byte> payload) : base(header, payload)
        {
        }

        public int FrameLength => Header.Size + Body.Length + 1;

        public void SetPeerId(uint peerId)
        {
            Header = new UdpHeaderBuilder()
                .From(Header)
                .WithPeerId(peerId)
                .Build();
        }

        public ArraySegment<byte> ToArraySegment()
        {
            var body = Body;
            var header = new UdpHeaderBuilder()
                .From(Header)
                .WithFragmented(0)
                .WithPayloadLength((ushort)body.Length)
                .Build();

            var buf = new byte[FrameLength];
            var span = buf.AsSpan();
            var offset = 0;

            header.WriteTo(span.Slice(offset, header.Size));
            offset += header.Size;

            if (body.Length > 0)
                body.Span.CopyTo(span.Slice(offset, body.Length));

            return new ArraySegment<byte>(buf, 0, buf.Length);
        }

        public List<ArraySegment<byte>> Split(uint fragId, ushort maxFragBodyLen)
        {
            if (maxFragBodyLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxFragBodyLen));

            var headerSize = Header.Size;
            const int fragHeaderSize = FragmentHeader.ByteSize;
            var bodyLen = Body.Length;
            var totalCount = (int)Math.Ceiling((double)bodyLen / maxFragBodyLen);
            if (totalCount > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxFragBodyLen), "Total count is too big (>255)");

            var result = new List<ArraySegment<byte>>(totalCount);
            var bodySpan = Body.Span;

            for (byte index = 0; index < totalCount; index++)
            {
                var offsetInBody = index * maxFragBodyLen;
                var remaining = bodyLen - offsetInBody;
                var fragLen = (ushort)Math.Min(remaining, maxFragBodyLen);

                var header = new UdpHeaderBuilder()
                    .From(Header)
                    .WithFragmented(1)
                    .WithPayloadLength(fragHeaderSize + fragLen)
                    .Build();

                var frameSize = headerSize + fragHeaderSize + fragLen + 1;
                var buf = new byte[frameSize];
                var span = buf.AsSpan();
                var offset = 0;

                header.WriteTo(span.Slice(offset, headerSize));
                offset += headerSize;

                var fh = new FragmentHeader(fragId, index, (byte)totalCount, fragLen);
                fh.WriteTo(span.Slice(offset, fragHeaderSize));
                offset += fragHeaderSize;

                bodySpan.Slice(offsetInBody, fragLen).CopyTo(span.Slice(offset, fragLen));

                result.Add(new ArraySegment<byte>(buf, 0, frameSize));
            }

            return result;
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
