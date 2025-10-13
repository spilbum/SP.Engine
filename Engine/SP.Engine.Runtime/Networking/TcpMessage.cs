using System;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : BaseMessage<TcpHeader>
    {
        public long SequenceNumber => Header.SequenceNumber;
        
        public TcpMessage()
        {
            
        }

        public TcpMessage(TcpHeader header, ArraySegment<byte> payload)
            : base(header, payload)
        {
        }
        
        public void SetSequenceNumber(long sequenceNumber)
        {
            Header = new TcpHeaderBuilder()
                .From(Header)
                .WithSequenceNumber(sequenceNumber)
                .Build();
        }
        
        public ArraySegment<byte> ToArraySegment()
        {
            var header = Header;
            var body = Body;
            var buf = new byte[header.Size + body.Count];
            var span = buf.AsSpan();
            
            // Tcp 헤더
            header.WriteTo(span);
            
            // 페이로드
            if (body.Count > 0 && body.Array != null)
                Buffer.BlockCopy(body.Array, body.Offset, buf, header.Size, body.Count);
            
            return new ArraySegment<byte>(buf);
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
