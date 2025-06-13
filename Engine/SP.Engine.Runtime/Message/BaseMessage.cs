using System;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Message
{
  public abstract class BaseMessage<THeader>: IMessage
        where THeader : IHeader
    {
        protected THeader Header { get; set; }
        public ArraySegment<byte> Payload { get; private set; }
        
        public long SequenceNumber => Header.SequenceNumber;
        public EProtocolId ProtocolId => Header.ProtocolId;
        public int Length => Payload.Count;
        private bool IsEncrypted => Header.Flags.HasFlag(EHeaderFlags.Encrypted);
        private bool IsCompressed => Header.Flags.HasFlag(EHeaderFlags.Compressed);

        protected BaseMessage()
        {
            
        }
        
        protected BaseMessage(THeader header, ArraySegment<byte> payload)
        {
            Header = header;
            Payload = payload;
        }

        protected abstract THeader CreateHeader(EProtocolId protocolId, EHeaderFlags flags, int payloadLength);

        
        protected byte[] GetBody()
            => Payload.AsSpan(Header.Length, Length - Header.Length).ToArray();
        
        public void Pack(IProtocolData data, byte[] sharedKey, PackOptions options)
        {
            var body = BinaryConverter.SerializeObject(data, data.GetType());
            if (body == null || body.Length == 0)
                throw new InvalidOperationException($"Failed to serialize protocol of type {data.GetType().FullName}");

            var flags = EHeaderFlags.None;
            if (options?.UseCompression ?? false)
            {
                var compressed = Compressor.Compress(body);
                var compressedSize = compressed.Length;
                var originalSize = body.Length;
                var ratio = (double)compressedSize / originalSize;
                if (compressedSize < originalSize && ratio < options.CompressionThreshold)
                {
                    body = compressed;
                    flags |= EHeaderFlags.Compressed;
                }
            }

            if (options?.UseEncryption ?? false)
            {
                if (sharedKey == null)
                    throw new InvalidOperationException("SharedKey cannot be null when encryption is enabled.");

                body = Encryptor.Encrypt(body, sharedKey);
                flags |= EHeaderFlags.Encrypted;
            }

            var header = CreateHeader(data.ProtocolId, flags, body.Length);
            var payload = new byte[header.Length + body.Length];
            header.WriteTo(payload.AsSpan(0, header.Length));
            body.CopyTo(payload.AsSpan(header.Length, body.Length));
            
            Header = header;
            Payload = new ArraySegment<byte>(payload);
        }

        public IProtocolData Unpack(Type type, byte[] sharedKey)
        {
            var body = GetBody();
            if (IsEncrypted)
            {
                if (sharedKey == null)
                    throw new InvalidOperationException("SharedKey cannot be null.");
                body = Encryptor.Decrypt(body, sharedKey);
            }

            if (IsCompressed)
                body = Compressor.Decompress(body);
            return BinaryConverter.DeserializeObject(body, type) as IProtocolData;
        }
    }
}
