using System.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : MessageBase<TcpHeader>
    {
        public TcpMessage()
        {
        }

        public TcpMessage(TcpHeader header, IMemoryOwner<byte> bufferOwner) : base(header, bufferOwner)
        {
        }

        protected override int HeaderLength => TcpHeader.ByteSize;

        public uint SequenceNumber => Header.SequenceNumber;

        public void SetSequenceNumber(uint sequenceNumber)
        {
            Header = new TcpHeaderBuilder()
                .From(Header)
                .WithSequenceNumber(sequenceNumber)
                .Build();
            
            UpdateHeaderInBuffer();
        }

        protected override TcpHeader CreateHeader(HeaderFlags flags, ushort protocolId, int payloadLength)
        {
            return new TcpHeaderBuilder()
                .From(Header)
                .AddFlag(flags)
                .WithProtocolId(protocolId)
                .WithPayloadLength(payloadLength)
                .Build();
        }
    }
}
