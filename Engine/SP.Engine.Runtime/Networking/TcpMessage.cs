
namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : MessageBase<TcpHeader, TcpMessage>
    {
        protected override int HeaderLength => TcpHeader.ByteSize;

        public uint SequenceNumber => _header.SequenceNumber;
        
        public void SetSequenceNumber(uint sequenceNumber)
        {
            _header = new TcpHeader(
                _header.Flags,
                sequenceNumber,
                _header.ProtocolId,
                _header.PayloadLength
            );
            
            UpdateHeaderInBuffer();
        }

        protected override TcpHeader CreateHeader(HeaderFlags flags, ushort protocolId, int payloadLength)
        {
            return new TcpHeader(
                flags,
                0,
                protocolId,
                payloadLength
            );
        }
    }
}
