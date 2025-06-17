using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : BaseMessage<TcpHeader>
    {
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
        
        protected override TcpHeader CreateHeader(EProtocolId protocolId, EHeaderFlags flags, int payloadLength)
        {
            return new TcpHeaderBuilder()
                .From(Header)
                .WithProtocolId(protocolId)
                .WithPayloadLength(payloadLength)
                .AddFlag(flags)
                .Build();
        }
    }
}
