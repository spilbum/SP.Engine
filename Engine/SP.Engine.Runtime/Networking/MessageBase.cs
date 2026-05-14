using System;
using System.Buffers;
using System.Threading;
using SP.Core;
using SP.Core.Logging;
using SP.Core.Serialization;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public abstract class MessageBase<THeader> : IMessage where THeader : IHeader
    {
        private int _refCount;
        private IMemoryOwner<byte> _bodyOwner;
        protected THeader Header { get; set; }

        public int BodyLength { get; private set; }
        public ushort Id => Header?.ProtocolId ?? 0;
        
        protected MessageBase()
        {
        }

        protected MessageBase(THeader header, IMemoryOwner<byte> bodyOwner, int bodyLength)
        {
            Header = header;
            _bodyOwner = bodyOwner;
            BodyLength = bodyLength;
            Retain();
        }
        
        protected Span<byte> BodySpan => _bodyOwner != null ? _bodyOwner.Memory.Span[..BodyLength] : Span<byte>.Empty;

        public void Retain()
        {
            Interlocked.Increment(ref _refCount);
        }

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _refCount) != 0) return;
            _bodyOwner?.Dispose();
            _bodyOwner = null;
        }

        public void Serialize(
            IProtocolData protocol,
            IPolicy policy = null, 
            IEncryptor encryptor = null, 
            ICompressor compressor = null)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));

            var buffer = new PooledBuffer();
            var writer = new NetWriter(buffer);
            protocol.Serialize(ref writer);   

            var bufferOwner = buffer;
            var written = writer.WrittenCount;
            var flags = HeaderFlags.None;

            try
            {
                if (policy is { UseCompress: true } && compressor != null && written >= policy.CompressionThreshold)
                {
                    var maxLen = compressor.GetMaxCompressedLength(written);
                    var compressedOwner = new PooledBuffer(maxLen);

                    written = compressor.Compress(bufferOwner.Memory.Span[..written],
                        compressedOwner.Memory.Span);

                    bufferOwner.Dispose();
                    bufferOwner = compressedOwner;
                    flags |= HeaderFlags.Compressed;
                }
                
                if (policy is { UseEncrypt: true } && encryptor != null)
                {
                    var maxLen = encryptor.GetCiphertextLength(written);
                    var encryptedOwner = new PooledBuffer(maxLen);

                    written = encryptor.Encrypt(bufferOwner.Memory.Span[..written],
                        encryptedOwner.Memory.Span);

                    bufferOwner.Dispose();
                    bufferOwner = encryptedOwner;
                    flags |= HeaderFlags.Encrypted;
                }

                _bodyOwner = bufferOwner;
                BodyLength = written;
                Header = CreateHeader(flags, protocol.Id, BodyLength);
                Retain();
            }
            catch
            {
                bufferOwner.Dispose();
                throw;
            }
        }

        public TProtocol Deserialize<TProtocol>(IEncryptor encryptor, ICompressor compressor)
            where TProtocol : IProtocolData
        {
            if (_bodyOwner == null) return default;

            var bodySpan = BodySpan;
            IMemoryOwner<byte> tempOwner = null;

            try
            {
                if (HasFlag(HeaderFlags.Encrypted) && encryptor != null)
                {
                    var maxLen = encryptor.GetPlaintextLength(bodySpan.Length);
                    var decryptedOwner = new PooledBuffer(maxLen);

                    var written = encryptor.Decrypt(bodySpan, decryptedOwner.Memory.Span);
                    
                    bodySpan = decryptedOwner.Memory.Span[..written];
                    tempOwner = decryptedOwner;
                }

                if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                {
                    var decompressedOwner = new PooledBuffer(compressor.GetDecompressedLength(bodySpan));
                    var written = compressor.Decompress(bodySpan, decompressedOwner.Memory.Span);
                    
                    tempOwner?.Dispose();
                    
                    bodySpan = decompressedOwner.Memory.Span[..written];
                    tempOwner = decompressedOwner;
                }

                var reader = new NetReader(bodySpan);
                return NetSerializer.Deserialize<TProtocol>(ref reader);
            }
            finally
            {
               tempOwner?.Dispose();
            }
        }

        private bool HasFlag(HeaderFlags flag) => Header != null && Header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort protocolId, int bodyLength);
    }
}
