using System;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : IMessage
    {
        private const double CompressionThreshold = 0.9;
        
        public long SequenceNumber => _header.SequenceNumber;
        public EProtocolId ProtocolId => _header.ProtocolId;

        private readonly TcpHeader _header;
        private byte[] _payload;
        
        private void SetFlag(EMessageFlags flag)
            => _header.Flags |= flag;
        
        private bool HasFlag(EMessageFlags flag)
            => _header.Flags.HasFlag(flag);

        public TcpMessage()
        {
            _header = new TcpHeader();
        }
        
        public TcpMessage(TcpHeader header, byte[] payload)
        {
            _header = header;
            _payload = payload;
        }
        
        public void SetSequenceNumber(long sequenceNumber)
        {
            _header.SequenceNumber = sequenceNumber;
        }
        
        public void SerializeProtocol(IProtocolData data, byte[] sharedKey = null)
        {
            var payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (payload == null || payload.Length == 0)
                throw new InvalidOperationException($"Failed to serialize protocol of type {data.GetType().FullName}");
            
            var compressed = Compressor.Compress(payload);
            var ratio = (double)compressed.Length / payload.Length;
            if (ratio < CompressionThreshold)
            {
                SetFlag(EMessageFlags.Compressed);
                payload = compressed;
            }

            if (data.IsEncrypt)
            {
                SetFlag(EMessageFlags.Encrypted);
                payload = Encryptor.Encrypt(payload, sharedKey);
            }
            
            _header.ProtocolId = data.ProtocolId;
            _header.PayloadLength = payload.Length;
            _payload = payload;
        }

        public IProtocolData DeserializeProtocol(Type type, byte[] sharedKey = null)
        {
            if (_payload == null || _payload.Length == 0)
                return null;
            
            var payload = _payload;
            if (HasFlag(EMessageFlags.Encrypted))
                payload = Encryptor.Decrypt(payload, sharedKey);

            if (HasFlag(EMessageFlags.Compressed))
                payload = Compressor.Decompress(payload);
            
            return BinaryConverter.DeserializeObject(payload, type) as IProtocolData;
        }

        public byte[] ToArray()
        {
            var totalSize = TcpHeader.HeaderSize + (_payload?.Length ?? 0);
            var buffer = new byte[totalSize];
            _header.WriteTo(buffer.AsSpan(0, TcpHeader.HeaderSize));
            _payload?.CopyTo(buffer.AsSpan(TcpHeader.HeaderSize, _payload.Length));
            return buffer;
        }
    }
}
