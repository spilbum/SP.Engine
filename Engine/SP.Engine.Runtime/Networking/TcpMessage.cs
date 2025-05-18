using System;
using System.Collections.Generic;
using SP.Common.Buffer;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;
using ArgumentNullException = System.ArgumentNullException;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : IMessage
    {
        public static bool TryParse(BinaryBuffer buffer, out TcpMessage message)
        {
            message = null;
            
            if (!TcpHeader.TryValidateLength(buffer, out _))
                return false;

            if (!TcpHeader.TryParse(buffer, out var header))
                return false;

            var payload = buffer.ReadBytes(header.PayloadLength);
            message = new TcpMessage { _header = header, _payload = payload };
            return true;
        }
        
        private const double CompressionThreshold = 0.9;
        
        public long SequenceNumber => _header.SequenceNumber;
        public EProtocolId ProtocolId => _header.ProtocolId;

        private TcpHeader _header = new TcpHeader();
        private byte[] _payload;

        private void EnableEncryption() => SetFlag(EMessageFlags.Encrypted);
        private void EnableCompression() => SetFlag(EMessageFlags.Compressed);
        private bool IsEncrypted => HasFlag(EMessageFlags.Encrypted);
        private bool IsCompressed => HasFlag(EMessageFlags.Compressed);
        
        private void SetFlag(EMessageFlags flag)
            => _header.Flags |= flag;
        
        private bool HasFlag(EMessageFlags flag)
            => _header.Flags.HasFlag(flag);

        public void SetSequenceNumber(long sequenceNumber)
        {
            _header.SequenceNumber = sequenceNumber;
        }
        
        public void SerializeProtocol(IProtocolData data, byte[] sharedKey = null)
        {
            _payload = BinaryConverter.SerializeObject(data, data.GetType())
                      ?? throw new InvalidOperationException($"Failed to serialize protocol of type {data.GetType().FullName}");

            _payload = CompressPayload(_payload);
            if (data.IsEncrypt)
                _payload = EncryptPayload(_payload, sharedKey);
            
            _header.ProtocolId = data.ProtocolId;
            _header.PayloadLength = _payload.Length;
        }

        private byte[] CompressPayload(byte[] payload)
        {
            var compressed = Compressor.Compress(payload);
            var ratio = (double)compressed.Length / payload.Length;
            if (ratio >= CompressionThreshold) return payload;
            EnableCompression();
            return compressed;
        }
        
        private byte[] EncryptPayload(byte[] payload, byte[] sharedKey)
        {
            if (sharedKey == null || sharedKey.Length == 0)
                throw new ArgumentNullException(nameof(sharedKey), "SharedKey cannot be null or empty");
            EnableEncryption();
            return Encryptor.Encrypt(payload, sharedKey);
        }

        public IProtocolData DeserializeProtocol(Type type, byte[] sharedKey = null)
        {
            if (_payload == null || _payload.Length == 0)
                return null;
            var payload = _payload;
            payload = DecryptPayload(payload, sharedKey);
            payload = DecompressPayload(payload);
            return BinaryConverter.DeserializeObject(payload, type) as IProtocolData;
        }

        private byte[] DecryptPayload(byte[] payload, byte[] sharedKey)
        {
            if (!IsEncrypted) return payload;
            if (sharedKey == null || sharedKey.Length == 0)
                throw new ArgumentNullException(nameof(sharedKey), "SharedKey cannot be null or empty");
            return Encryptor.Decrypt(payload, sharedKey);
        }

        private byte[] DecompressPayload(byte[] payload)
            => IsCompressed ? Compressor.Decompress(payload) : payload;
        
        public byte[] ToArray()
        {
            var totalSize = TcpHeader.HeaderSize + (_payload?.Length ?? 0);
            var buffer = new byte[totalSize];
            var span = buffer.AsSpan();
            _header.WriteTo(span[..TcpHeader.HeaderSize]);
            _payload?.CopyTo(span[TcpHeader.HeaderSize..]);
            return buffer;
        }
    }
}
