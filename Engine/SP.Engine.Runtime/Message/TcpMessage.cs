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
        
        public void SetSequenceNumber(long sequenceNumber)
        {
            _header.SequenceNumber = sequenceNumber;
        }
        
        public void Pack(IProtocolData data, byte[] sharedKey = null, byte[] hmacKey = null)
        {
            var payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (payload == null || payload.Length == 0)
                throw new InvalidOperationException($"Failed to serialize protocol of type {data.GetType().FullName}");
            
            // 메시지 압축
            var compressed = Compressor.Compress(payload);
            var ratio = (double)compressed.Length / payload.Length;
            if (ratio < CompressionThreshold)
            {
                payload = compressed;
                SetFlag(EMessageFlags.Compressed);
            }

            // 메시지 암호화
            if (data.IsEncrypt)
            {
                if (sharedKey == null)
                    throw new InvalidOperationException("SharedKey cannot be null.");
                
                payload = Encryptor.Encrypt(payload, sharedKey);
                SetFlag(EMessageFlags.Encrypted);
            }

            // 메시지 검증 정보 추가
            if (hmacKey != null)
            {
                var hmac = DhUtil.ComputeHmac(hmacKey, payload);
                
                var result = new byte[payload.Length + hmac.Length];
                Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
                Buffer.BlockCopy(hmac, 0, result,  payload.Length, hmac.Length);
                
                payload = result;
                SetFlag(EMessageFlags.Hmac);
            }
            
            _header.ProtocolId = data.ProtocolId;
            _header.PayloadLength = payload.Length;
            _payload = payload;
        }

        public IProtocolData Unpack(Type type, byte[] sharedKey = null, byte[] hmacKey = null)
        {
            var payload = _payload;
            if (payload == null || payload.Length == 0)
                return null;

            // 메시지 검증
            if (HasFlag(EMessageFlags.Hmac))
            {
                if (hmacKey == null || payload.Length < DhUtil.HmacSize)
                    return null;
                
                var content = payload[..^DhUtil.HmacSize];
                var hmac = payload[^DhUtil.HmacSize..];

                if (!DhUtil.VerifyHmac(hmacKey, content, hmac)) 
                    throw new InvalidOperationException($"Hmac validation failed. protocolId={_header.ProtocolId}, sequenceNumber={_header.SequenceNumber}");
                
                payload = content;
            }

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
            _payload?.CopyTo(buffer.AsSpan(TcpHeader.HeaderSize, _payload.Length));
            return buffer;
        }
    }
}
