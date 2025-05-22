using System;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Message
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
        
        public void EnsureSequenceNumber(long sequenceNumber)
        {
            if (_header.SequenceNumber == 0)
                _header.SequenceNumber = sequenceNumber;
        }
        
        public void Pack(IProtocolData data, byte[] sharedKey = null, PackOptions options = null)
        {
            var payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (payload == null || payload.Length == 0)
                throw new InvalidOperationException($"Failed to serialize protocol of type {data.GetType().FullName}");
            
            // 메시지 압축
            if (options?.UseCompression ?? false)
            {
                var compressed = Compressor.Compress(payload);
                var ratio = (double)compressed.Length / payload.Length;
                
                if (ratio < options.CompressionThreshold)
                {
                    payload = compressed;
                    SetFlag(EMessageFlags.Compressed);
                }
            }

            // 메시지 암호화
            if (options?.UseEncryption ?? false)
            {
                if (sharedKey == null)
                    throw new InvalidOperationException("SharedKey cannot be null when encryption is enabled.");

                payload = Encryptor.Encrypt(payload, sharedKey);
                SetFlag(EMessageFlags.Encrypted);
            }

            _header.ProtocolId = data.ProtocolId;
            _header.PayloadLength = payload.Length;
            _payload = payload;
        }

        public IProtocolData Unpack(Type type, byte[] sharedKey = null)
        {
            var payload = _payload;
            if (payload == null || payload.Length == 0)
                return null;

            // 메시지 복호화
            if (HasFlag(EMessageFlags.Encrypted))
            {
                if (sharedKey == null)
                    throw new InvalidOperationException("SharedKey cannot be null.");
                
                payload = Encryptor.Decrypt(payload, sharedKey);
            }

            // 메시지 압축 해제
            if (HasFlag(EMessageFlags.Compressed))
                payload = Compressor.Decompress(payload);
            
            return BinaryConverter.DeserializeObject(payload, type) as IProtocolData;
        }

        public byte[] ToArray()
        {
            var totalSize = TcpHeader.HeaderSize + (_payload?.Length ?? 0);
            var buffer = new byte[totalSize];
            _header.WriteTo(buffer.AsSpan(0, TcpHeader.HeaderSize));
            if (_payload != null)
                Buffer.BlockCopy(_payload, 0, buffer, TcpHeader.HeaderSize, _payload.Length);
            return buffer;
        }
    }
}
