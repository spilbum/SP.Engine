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
            var bodyLen = Body.Count;
            var buf = new byte[Header.Size + bodyLen];
            var span = buf.AsSpan();
            Header.WriteTo(span);
            
            if (bodyLen > 0 && Body.Array != null)
                Buffer.BlockCopy(Body.Array, Body.Offset, buf, Header.Size, bodyLen);
            
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
