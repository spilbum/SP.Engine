using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct TcpHeader : IHeader
    {
        public const int ByteSize = 1 + 4 + 2 + 4; // 11 bytes

        public HeaderFlags Flags { get; }
        public uint SequenceNumber { get; }
        public ushort MsdId { get; }
        public int PayloadLength { get; }
        public int Size { get; }

        public TcpHeader(HeaderFlags flags, uint sequenceNumber, ushort msdId, int payloadLength)
        {
            Flags = flags;
            SequenceNumber = sequenceNumber;
            MsdId = msdId;
            PayloadLength = payloadLength;
            Size = ByteSize;
        }

        public bool HasFlag(HeaderFlags flags)
        {
            return (Flags & flags) != 0;
        }

        public void WriteTo(Span<byte> destination)
        {
            destination[0] = (byte)Flags;
            destination.WriteUInt32(1, SequenceNumber);
            destination.WriteUInt16(5, MsdId);
            destination.WriteInt32(7, PayloadLength);
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
            var sequenceNumber = source.ReadUInt32(1);
            var id = source.ReadUInt16(5);
            var len = source.ReadInt32(7);
            header = new TcpHeader(flags, sequenceNumber, id, len);
            consumed = ByteSize;
            return true;
        }
    }

    public class TcpHeaderBuilder
    {
        private HeaderFlags _flags;
        private ushort _id;
        private uint _sequenceNumber;
        private int _payloadLength;

        public TcpHeaderBuilder From(TcpHeader header)
        {
            _flags = header.Flags;
            _sequenceNumber = header.SequenceNumber;
            _id = header.MsdId;
            _payloadLength = header.PayloadLength;
            return this;
        }

        public TcpHeaderBuilder WithSequenceNumber(uint sequenceNumber)
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
        {
            return new TcpHeader(_flags, _sequenceNumber, _id, _payloadLength);
        }
    }
}
