using System;
using System.IO;
using System.Security.Cryptography;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Networking
{
    public abstract class BaseMessage<THeader> : IMessage
        where THeader : IHeader
    {
        protected BaseMessage()
        {
        }

        protected BaseMessage(THeader header, byte[] body)
        {
            Header = header;
            Body = new ReadOnlyMemory<byte>(body);
        }

        protected BaseMessage(THeader header, ReadOnlyMemory<byte> body)
        {
            Header = header;
            Body = body;
        }

        protected THeader Header { get; set; }
        protected ReadOnlyMemory<byte> Body { get; private set; }

        public ushort Id => Header.MsdId;

        public TProtocol Deserialize<TProtocol>(IEncryptor encryptor, ICompressor compressor)
            where TProtocol : IProtocolData
        {
            var isEnc = HasFlag(HeaderFlags.Encrypted);
            var isComp = HasFlag(HeaderFlags.Compressed);
            if (!isEnc && !isComp)
            {
                var reader = new NetReader(Body.Span);
                return (TProtocol)NetSerializer.Deserialize(ref reader, typeof(TProtocol));
            }

            var bodySpan = Body.Span;
            byte[] decrypted = null;
            byte[] decompressed = null;

            try
            {
                if (isEnc)
                {
                    if (encryptor == null)
                        throw new InvalidDataException("Encrypted payload but no encryptor provided.");
                    decrypted = encryptor.Decrypt(bodySpan);
                    bodySpan = decrypted;
                }

                if (isComp)
                {
                    if (compressor == null)
                        throw new InvalidDataException("Compressed payload but no compressor provided.");
                    decompressed = compressor.Decompress(bodySpan);
                    bodySpan = decompressed;
                }

                var reader = new NetReader(bodySpan);
                return (TProtocol)NetSerializer.Deserialize(ref reader, typeof(TProtocol));
            }
            finally
            {
                if (decrypted != null) CryptographicOperations.ZeroMemory(decrypted);
                if (decompressed != null) CryptographicOperations.ZeroMemory(decompressed);
            }
        }

        private bool HasFlag(HeaderFlags flag)
        {
            return Header != null && Header.HasFlag(flag);
        }

        protected abstract THeader CreateHeader(HeaderFlags flags, ushort id, int payloadLength);

        public void Serialize(IProtocolData protocol, IPolicy policy, IEncryptor encryptor, ICompressor compressor)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));

            var w = new NetWriter();
            NetSerializer.Serialize(ref w, protocol.GetType(), protocol);
            ReadOnlyMemory<byte> payload = w.ToArray();

            var flags = HeaderFlags.None;

            if (policy.UseCompress && payload.Length >= policy.CompressionThreshold && compressor != null)
            {
                payload = compressor.Compress(payload.Span);
                flags |= HeaderFlags.Compressed;
            }

            if (policy.UseEncrypt && encryptor != null)
            {
                payload = encryptor.Encrypt(payload.Span);
                flags |= HeaderFlags.Encrypted;
            }

            Header = CreateHeader(flags, protocol.ProtocolId, payload.Length);
            Body = payload;
        }
    }
}
