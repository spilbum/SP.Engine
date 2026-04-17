using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct TcpHeader : IHeader
    {
        public const int ByteSize = 1 + 4 + 4 + 2 + 4; // 15 bytes

        public HeaderFlags Flags { get; }
        public uint SequenceNumber { get; }
        public uint AckNumber { get; }
        public ushort Id { get; }
        public int BodyLength { get; }

        public TcpHeader(HeaderFlags flags, uint sequenceNumber, uint ackNumber, ushort id, int bodyLength)
        {
            Flags = flags;
            SequenceNumber = sequenceNumber;
            AckNumber = ackNumber;
            Id = id;
            BodyLength = bodyLength;
        }

        public bool HasFlag(HeaderFlags flags)
        {
            return (Flags & flags) != 0;
        }

        public void WriteTo(Span<byte> destination)
        {
            destination[0] = (byte)Flags;
            destination.WriteUInt32(1, SequenceNumber);
            destination.WriteUInt32(5, AckNumber);
            destination.WriteUInt16(9, Id);
            destination.WriteInt32(11, BodyLength);
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
            var ackNumber = source.ReadUInt32(5);
            var id = source.ReadUInt16(9);
            var bodyLength = source.ReadInt32(11);
            header = new TcpHeader(flags, sequenceNumber, ackNumber, id, bodyLength);
            consumed = ByteSize;
            return true;
        }
    }

    public class TcpHeaderBuilder
    {
        private HeaderFlags _flags;
        private ushort _id;
        private uint _sequenceNumber;
        private uint _ackNumber;
        private int _bodyLength;

        public TcpHeaderBuilder From(TcpHeader header)
        {
            _flags = header.Flags;
            _sequenceNumber = header.SequenceNumber;
            _ackNumber = header.AckNumber;
            _id = header.Id;
            _bodyLength = header.BodyLength;
            return this;
        }

        public TcpHeaderBuilder WithSequenceNumber(uint sequenceNumber)
        {
            _sequenceNumber = sequenceNumber;
            return this;
        }

        public TcpHeaderBuilder WithAckNumber(uint ackNumber)
        {
            _ackNumber = ackNumber;
            return this;
        }

        public TcpHeaderBuilder WithProtocolId(ushort id)
        {
            _id = id;
            return this;
        }

        public TcpHeaderBuilder AddFlag(HeaderFlags flags)
        {
            _flags |= flags;
            return this;
        }

        public TcpHeaderBuilder WithBodyLength(int bodyLength)
        {
            _bodyLength = bodyLength;
            return this;
        }

        public TcpHeader Build()
        {
            return new TcpHeader(_flags, _sequenceNumber, _ackNumber, _id, _bodyLength);
        }
    }
}
