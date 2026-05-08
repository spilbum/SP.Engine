using System;
using System.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : BaseMessage<TcpHeader>
    {
        public TcpMessage()
        {
        }

        public TcpMessage(TcpHeader header, IMemoryOwner<byte> bodyOwner, int bodyLength) 
            : base(header, bodyOwner, bodyLength)
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
            var total = hSize + BodyLength;

            if (destination.Length < total) return 0;

            Header.WriteTo(destination[..hSize]);
            if (BodyLength > 0)
            {
                BodySpan.CopyTo(destination.Slice(hSize, BodyLength));
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
