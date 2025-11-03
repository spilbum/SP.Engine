using System;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : BaseMessage<TcpHeader>
    {
        public TcpMessage()
        {
        }

        public TcpMessage(TcpHeader header, byte[] body) : base(header, body)
        {
        }

        public TcpMessage(TcpHeader header, ReadOnlyMemory<byte> payload) : base(header, payload)
        {
        }

        public long SequenceNumber => Header.SequenceNumber;

        public void SetSequenceNumber(long sequenceNumber)
        {
            Header = new TcpHeaderBuilder()
                .From(Header)
                .WithSequenceNumber(sequenceNumber)
                .Build();
        }

        public ArraySegment<byte> ToArraySegment()
        {
            var hSize = Header.Size;
            var bLen = Body.Length;
            var buf = new byte[hSize + bLen];
            var span = buf.AsSpan();

            Header.WriteTo(span[..hSize]);
            if (bLen > 0)
                Body.Span.CopyTo(span.Slice(hSize, bLen));

            return new ArraySegment<byte>(buf, 0, buf.Length);
        }

        protected override TcpHeader CreateHeader(HeaderFlags flags, ushort id, int payloadLength)
        {
            return new TcpHeaderBuilder()
                .From(Header)
                .AddFlag(flags)
                .WithId(id)
                .WithPayloadLength(payloadLength)
                .Build();
        }
    }
}
