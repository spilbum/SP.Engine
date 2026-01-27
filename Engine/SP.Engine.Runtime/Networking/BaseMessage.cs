using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using SP.Core.Serialization;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

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

        public void Serialize(IProtocolData protocol, IPolicy policy, IEncryptor encryptor, ICompressor compressor)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));

            using var w = new NetWriter();
            NetSerializer.Serialize(w, protocol.GetType(), protocol);
            
            var dataSpan = w.WrittenSpan;
            var flags = HeaderFlags.None;

            var useComp = policy.UseCompress && compressor != null && dataSpan.Length >= policy.CompressionThreshold;
            var useEncrypt = policy.UseEncrypt && encryptor != null;

            if (!useComp && !useEncrypt)
            {
                var bodyBytes = dataSpan.ToArray();
                
                Header = CreateHeader(flags, protocol.ProtocolId, bodyBytes.Length);
                Body = new ReadOnlyMemory<byte>(bodyBytes);
                return;
            }

            byte[] rentBuf1 = null;
            byte[] rentBuf2 = null;

            try
            {
                if (useComp)
                {
                    var maxLen = compressor.GetMaxCompressedLength(dataSpan.Length);
                    rentBuf1 = ArrayPool<byte>.Shared.Rent(maxLen);

                    var written = compressor.Compress(dataSpan, rentBuf1);

                    dataSpan = new ReadOnlySpan<byte>(rentBuf1, 0, written);
                    flags |= HeaderFlags.Compressed;
                }

                if (useEncrypt)
                {
                    var maxLen = encryptor.GetCiphertextLength(dataSpan.Length);
                    rentBuf2 = ArrayPool<byte>.Shared.Rent(maxLen);

                    var written = encryptor.Encrypt(dataSpan, rentBuf2);

                    dataSpan = new ReadOnlySpan<byte>(rentBuf2, 0, written);
                    flags |= HeaderFlags.Encrypted;
                }

                var bodyBytes = dataSpan.ToArray();

                Header = CreateHeader(flags, protocol.ProtocolId, bodyBytes.Length);
                Body = new ReadOnlyMemory<byte>(bodyBytes);
            }
            finally
            {
                if (rentBuf1 != null) ArrayPool<byte>.Shared.Return(rentBuf1);
                if (rentBuf2 != null) ArrayPool<byte>.Shared.Return(rentBuf2);
            }
        }

        public TProtocol Deserialize<TProtocol>(IEncryptor encryptor, ICompressor compressor)
            where TProtocol : IProtocolData
        {
            var dataSpan = Body.Span;
            
            byte[] rentBuf1 = null;
            byte[] rentBuf2 = null;

            try
            {
                if (HasFlag(HeaderFlags.Encrypted) && encryptor != null)
                {
                    var maxLen = encryptor.GetPlaintextLength(dataSpan.Length);
                    rentBuf1 = ArrayPool<byte>.Shared.Rent(maxLen);
                    
                    var written = encryptor.Decrypt(dataSpan, rentBuf1);
                    dataSpan = new ReadOnlySpan<byte>(rentBuf1, 0, written);
                }

                if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                {
                    var decompressedLength = compressor.GetDecompressedLength(dataSpan);
                    rentBuf2 = ArrayPool<byte>.Shared.Rent(decompressedLength);
                    
                    var written = compressor.Decompress(dataSpan, rentBuf2);
                    dataSpan = new ReadOnlySpan<byte>(rentBuf2, 0, written);
                }
                
                var reader = new NetReader(dataSpan);
                return (TProtocol)NetSerializer.Deserialize(ref reader, typeof(TProtocol));
            }
            finally
            {
                if (rentBuf1 != null) ArrayPool<byte>.Shared.Return(rentBuf1);
                if (rentBuf2 != null) ArrayPool<byte>.Shared.Return(rentBuf2);
            }
        }

        private bool HasFlag(HeaderFlags flag) => Header != null && Header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort id, int payloadLength);
    }
}
