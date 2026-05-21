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
            
            var maxPayloadLength = policy?.MaxPayloadLength ?? 65536;
            if (policy is { UseCompress: true } && compressor != null)
            {
                maxPayloadLength = compressor.GetMaxCompressedLength(maxPayloadLength);
            }

            if (policy is { UseEncrypt: true } && encryptor != null)
            {
                maxPayloadLength = encryptor.GetCiphertextLength(maxPayloadLength);
            }

            var bufferOwner = new PooledBuffer(maxPayloadLength);
            var span = bufferOwner.Memory.Span;
            var writer = new NetWriter(span);
            protocol.Serialize(ref writer);      
            
            var written = writer.WrittenCount;
            var flags = HeaderFlags.None;
            
            try
            {
                if (policy is { UseCompress: true } && compressor != null && written >= policy.CompressionThreshold)
                {
                    var srcSpan = span[..written];
                    var destSpan = span[written..];
                        
                    var compressedLen = compressor.Compress(srcSpan, destSpan);

                    destSpan[..compressedLen].CopyTo(span);
                    written = compressedLen;
                    flags |= HeaderFlags.Compressed;
                }   

                if (policy is { UseEncrypt: true } && encryptor != null)
                {
                    var srcSpan = span[..written];
                    var destSpan = span[written..];
                        
                    var encryptedLen = encryptor.Encrypt(srcSpan, destSpan);

                    destSpan[..encryptedLen].CopyTo(span);
                    written = encryptedLen;
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

            var srcSpan = BodySpan;
            if (srcSpan.IsEmpty) return default;

            var maxPlaintextLen = srcSpan.Length;
            if (HasFlag(HeaderFlags.Encrypted) && compressor != null)
            {
                maxPlaintextLen = encryptor.GetPlaintextLength(maxPlaintextLen);
            }

            if (HasFlag(HeaderFlags.Compressed) && compressor != null)
            {
                maxPlaintextLen = compressor.GetDecompressedLength(srcSpan);
            }
            
            var owner = new PooledBuffer(maxPlaintextLen);
            var span = owner.Memory.Span;

            try
            {
                if (HasFlag(HeaderFlags.Encrypted) && encryptor != null)
                {
                    var written = encryptor.Decrypt(srcSpan, span);
                    srcSpan = span[..written];
                }

                if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                {
                    var destSpan = span[srcSpan.Length..];
                    var written = compressor.Decompress(srcSpan, destSpan);
                    srcSpan = destSpan[..written];
                }

                var reader = new NetReader(srcSpan);
                return NetSerializer.Deserialize<TProtocol>(ref reader);
            }
            finally
            {
                owner?.Dispose();
            }
        }

        private bool HasFlag(HeaderFlags flag) => Header != null && Header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort protocolId, int bodyLength);
    }
}
