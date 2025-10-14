using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct TcpHeader : IHeader
    {
        public const int ByteSize = 1 + 8 + 2 + 4; // 15 bytes
        
        public HeaderFlags Flags { get; }
        public long SequenceNumber { get; }
        public ushort MsdId { get; }
        public int PayloadLength { get; }
        public int Size { get; }

        public TcpHeader(HeaderFlags flags, long sequenceNumber, ushort msdId, int payloadLength)
        {
            Flags = flags;
            SequenceNumber = sequenceNumber;
            MsdId = msdId;
            PayloadLength = payloadLength;
            Size = ByteSize;
        }

        public void WriteTo(Span<byte> destination)
        {
            destination[0] = (byte)Flags;
            destination.WriteInt64(1, SequenceNumber);
            destination.WriteUInt16(9, MsdId);
            destination.WriteInt32(11, PayloadLength);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out TcpHeader header, out int consumed)
        {
            if (source.Length < ByteSize)
            {
                header = default;
                consumed = 0;   
                return false;
            }
            
            var flags = (HeaderFlags)source[0];
            var sequenceNumber = source.ReadInt64(1);
            var id = source.ReadUInt16(9);
            var len = source.ReadInt32(11);
            header = new TcpHeader(flags, sequenceNumber, id, len);
            consumed = ByteSize;
            return true;
        }
    }

    public class TcpHeaderBuilder
    {
        private HeaderFlags _flags;
        private long _sequenceNumber;
        private ushort _id;
        private int _payloadLength;

        public TcpHeaderBuilder From(TcpHeader header)
        {
            _flags = header.Flags;
            _sequenceNumber = header.SequenceNumber;
            _id = header.MsdId;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public TcpHeaderBuilder WithSequenceNumber(long sequenceNumber)
        {
            _sequenceNumber = sequenceNumber;
            return this;
        }

        public TcpHeaderBuilder WithId(ushort id)
        {
            _id = id;
            return this;
        }

        public TcpHeaderBuilder AddFlag(HeaderFlags flags)
        {
            _flags |= flags;
            return this;
        }

        public TcpHeaderBuilder WithPayloadLength(int payloadLength)
        {
            _payloadLength = payloadLength;
            return this;
        }
        
        public TcpHeader Build()
            => new TcpHeader(_flags, _sequenceNumber, _id, _payloadLength);
    }
}
