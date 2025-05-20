using System;
using SP.Common.Buffer;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Utilities;

namespace SP.Engine.Runtime.Message
{
    public class TcpHeader
    {
        private const int SequenceNumberOffset = 0;
        private const int ProtocolIdOffset = SequenceNumberOffset + sizeof(long);
        private const int FlagsOffset = ProtocolIdOffset + sizeof(ushort);
        private const int PayloadLengthOffset = FlagsOffset + sizeof(byte);
        public const int HeaderSize = PayloadLengthOffset + sizeof(int);
        
        public long SequenceNumber { get; set; }
        public EProtocolId ProtocolId { get; set; }
        public EMessageFlags Flags { get; set; }
        public int PayloadLength { get; set; }

        public TcpHeader()
        {
            
        }
        
        private TcpHeader(long sequenceNumber, ushort protocolId, byte flags, int payloadLength)
        {
            SequenceNumber = sequenceNumber;
            ProtocolId = (EProtocolId)protocolId;
            Flags = (EMessageFlags)flags;
            PayloadLength = payloadLength;
        }

        public void WriteTo(Span<byte> span)
        {
            span.WriteInt64(SequenceNumberOffset, SequenceNumber);
            span.WriteUInt16(ProtocolIdOffset, (ushort)ProtocolId);
            span[FlagsOffset] = (byte)Flags;
            span.WriteInt32(PayloadLengthOffset, PayloadLength);
        }
        
        public static bool TryParse(BinaryBuffer buffer, out TcpHeader header)
        {
            header = null;
            
            if (buffer.RemainSize < HeaderSize)
                return false;

            var span = buffer.Read(HeaderSize);
            var sequenceNumber = span.ReadInt64(SequenceNumberOffset);
            var protocolId = span.ReadUInt16(ProtocolIdOffset);
            var flags = span[FlagsOffset];
            if ((flags & ~(byte)EMessageFlags.All) != 0)
                return false;
            
            var payloadLength = span.ReadInt32(PayloadLengthOffset);
            header = new TcpHeader(sequenceNumber, protocolId, flags, payloadLength);
            return true;
        }
        
        public static bool TryValidateLength(BinaryBuffer buffer, out int totalLength)
        {
            totalLength = 0;
            
            if (buffer.RemainSize < HeaderSize)
                return false;
            
            var span = buffer.Peek(HeaderSize);
            var payloadLength = span.ReadInt32(PayloadLengthOffset);
            if (payloadLength > int.MaxValue - HeaderSize)
                return false;
            
            var length = HeaderSize + payloadLength; 
            if (buffer.RemainSize < length)
                return false;
            
            totalLength = length;
            return true;
        }
    }
}
