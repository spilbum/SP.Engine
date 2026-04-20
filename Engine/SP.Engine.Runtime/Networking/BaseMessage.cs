using System;
using System.Buffers;
using SP.Core;
using SP.Core.Serialization;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public abstract class BaseMessage<THeader> : IMessage where THeader : IHeader
    {
        protected THeader Header { get; set; }
        private RentedBuffer _body;
        public int BodyLength => _body.Length;
        protected ReadOnlySpan<byte> BodySpan => _body.Span;
        
        public ushort Id => Header.Id;
        
        protected BaseMessage()
        {
        }

        protected BaseMessage(THeader header, RentedBuffer body)
        {
            Header = header;
            _body = body;
        }

        public void Dispose()
        {
            Header = default;
            _body.Dispose();
        }
        
        public void Serialize(IProtocolData protocol, IPolicy policy, IEncryptor encryptor, ICompressor compressor)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));

            using var w = new NetWriter();
            NetSerializer.Serialize(w, protocol.GetType(), protocol);
            
            var bodySpan = w.WrittenSpan;
            var flags = HeaderFlags.None;

            byte[] rentBuf1 = null;
            byte[] rentBuf2 = null;
            
            try
            {
                if (policy.UseCompress && compressor != null && bodySpan.Length >= policy.CompressionThreshold)
                {
                    var maxLen = compressor.GetMaxCompressedLength(bodySpan.Length);
                    rentBuf1 = ArrayPool<byte>.Shared.Rent(maxLen);
                    // 압축
                    var count = compressor.Compress(bodySpan, rentBuf1);

                    bodySpan = new ReadOnlySpan<byte>(rentBuf1, 0, count);
                    flags |= HeaderFlags.Compressed;
                }

                if (policy.UseEncrypt && encryptor != null)
                {
                    var maxLen = encryptor.GetCiphertextLength(bodySpan.Length);
                    rentBuf2 = ArrayPool<byte>.Shared.Rent(maxLen);
                    // 암호화
                    var count = encryptor.Encrypt(bodySpan, rentBuf2);
                    
                    bodySpan = new ReadOnlySpan<byte>(rentBuf2, 0, count);
                    flags |= HeaderFlags.Encrypted;
                }

                byte[] bodyBytes;
                if (rentBuf2 != null)
                {
                    bodyBytes = rentBuf2;
                    rentBuf2 = null;
                }
                else if (rentBuf1 != null)
                {
                    bodyBytes = rentBuf1;
                    rentBuf1 = null;
                }
                else
                {
                    bodyBytes = ArrayPool<byte>.Shared.Rent(bodySpan.Length);
                    bodySpan.CopyTo(bodyBytes);
                }

                var bodyLength = bodySpan.Length;
                _body = new RentedBuffer(bodyBytes, bodyLength);
                Header = CreateHeader(flags, protocol.Id, bodyLength);
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
            byte[] rentBuf1 = null;
            byte[] rentBuf2 = null;

            try
            {
                var bodySpan = BodySpan;
                if (HasFlag(HeaderFlags.Encrypted) && encryptor != null)
                {
                    var maxLen = encryptor.GetPlaintextLength(bodySpan.Length);
                    rentBuf1 = ArrayPool<byte>.Shared.Rent(maxLen);
                    // 복호화
                    var count = encryptor.Decrypt(bodySpan, rentBuf1);
                    bodySpan = new ReadOnlySpan<byte>(rentBuf1, 0, count);
                }

                if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                {
                    var decompressedLength = compressor.GetDecompressedLength(bodySpan);
                    rentBuf2 = ArrayPool<byte>.Shared.Rent(decompressedLength);
                    // 압축 해제
                    var count = compressor.Decompress(bodySpan, rentBuf2);
                    bodySpan = new ReadOnlySpan<byte>(rentBuf2, 0, count);
                }
                
                var reader = new NetReader(bodySpan);
                return (TProtocol)NetSerializer.Deserialize(ref reader, typeof(TProtocol));
            }
            finally
            {
                if (rentBuf1 != null) ArrayPool<byte>.Shared.Return(rentBuf1);
                if (rentBuf2 != null) ArrayPool<byte>.Shared.Return(rentBuf2);
            }
        }

        private bool HasFlag(HeaderFlags flag) => Header != null && Header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort protocolId, int bodyLength);
    }
}
