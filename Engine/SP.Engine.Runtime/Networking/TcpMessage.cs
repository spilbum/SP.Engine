using System;
using System.Buffers;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : BaseMessage<TcpHeader>
    {
        public TcpMessage()
        {
        }

        public TcpMessage(TcpHeader header, IMemoryOwner<byte> bodyOwner) : base(header, bodyOwner)
        {
        }

        public uint SequenceNumber => Header.SequenceNumber;
        public uint AckNumber => Header.AckNumber;
        public int Size => TcpHeader.ByteSize + _bodyOwner?.Memory.Length ?? 0;

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
            var bodyLength = _bodyOwner?.Memory.Length ?? 0;
            var total = hSize + bodyLength;

            if (destination.Length < total) return 0;
            
            Header.WriteTo(destination[..hSize]);
            if (bodyLength > 0)
            {
                _bodyOwner?.Memory.Span.CopyTo(destination.Slice(hSize, bodyLength));
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
