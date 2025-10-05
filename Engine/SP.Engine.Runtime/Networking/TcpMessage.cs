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
