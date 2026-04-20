using System;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : BaseMessage<TcpHeader>
    {
        public TcpMessage()
        {
        }

        public TcpMessage(TcpHeader header, RentedBuffer body) : base(header, body)
        {
        }

        public uint SequenceNumber => Header.SequenceNumber;
        public uint AckNumber => Header.AckNumber;
        public int Size => TcpHeader.ByteSize + BodyLength;

        public void SetSequenceNumber(uint sequenceNumber)
        {
            Header = new TcpHeaderBuilder()
                .From(Header)
                .WithSequenceNumber(sequenceNumber)
                .Build();
        }

        public void SetAckNumber(uint ackNumber)
        {
            Header = new TcpHeaderBuilder()
                .From(Header)
                .WithAckNumber(ackNumber)
                .Build();
        }

        public int WriteTo(Span<byte> destination)
        {
            const int hSize = TcpHeader.ByteSize;
            var bLen = BodyLength;
            var total = hSize + bLen;

            if (destination.Length < total) return 0;
            
            Header.WriteTo(destination[..hSize]);
            if (bLen > 0)
            {
                BodySpan.CopyTo(destination.Slice(hSize, bLen));
            }
            return total;
        }
        
        protected override TcpHeader CreateHeader(HeaderFlags flags, ushort protocolId, int bodyLength)
        {
            return new TcpHeaderBuilder()
                .From(Header)
                .AddFlag(flags)
                .WithProtocolId(protocolId)
                .WithBodyLength(bodyLength)
                .Build();
        }
    }
}
