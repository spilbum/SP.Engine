using System;
using System.Buffers;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Message
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
        
        protected override byte[] GetBody()
            => Payload.AsSpan(TcpHeader.HeaderSize, Payload.Count - TcpHeader.HeaderSize).ToArray();

        protected override TcpHeader CreateHeader(EProtocolId protocolId, EHeaderFlags flags, int payloadLength)
        {
            return new TcpHeaderBuilder()
                .From(Header)
                .WithProtocolId(protocolId)
                .WithPayloadLength(payloadLength)
                .AddFlag(flags)
                .Build();
        }
        
        protected override ArraySegment<byte> BuildPayload(TcpHeader header, byte[] body)
        {
            var length = TcpHeader.HeaderSize + body.Length;
            var payload = ArrayPool<byte>.Shared.Rent(length);
            header.WriteTo(payload.AsSpan(0, TcpHeader.HeaderSize));
            body.CopyTo(payload.AsSpan(TcpHeader.HeaderSize, body.Length));
            return new ArraySegment<byte>(payload, 0, length);
        }
    }
}
