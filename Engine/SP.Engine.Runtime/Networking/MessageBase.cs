using System;
using System.Buffers;
using System.Threading;
using SP.Core;
using SP.Core.Buffers;
using SP.Core.Serialization;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public abstract class MessageBase<THeader> : IMessage where THeader : IHeader
    {
        private const int LIMIT_PAYLOAD_LENGTH = 64 * 1024;
        
        private int _refCount;
        private IMemoryOwner<byte> _bufferOwner;
        protected THeader Header { get; set; }

        public int PayloadLength => Header?.PayloadLength ?? 0;
        public int TotalLength => HeaderLength + PayloadLength;
        public ushort Id => Header?.ProtocolId ?? 0;
        
        protected abstract int HeaderLength { get; }
        
        protected MessageBase() { }

        protected MessageBase(THeader header, IMemoryOwner<byte> bufferOwner)
        {
            Header = header;
            _bufferOwner = bufferOwner;
            Retain();
        }

        protected Span<byte> PayloadSpan => _bufferOwner != null
            ? _bufferOwner.Memory.Span.Slice(HeaderLength, PayloadLength)
            : Span<byte>.Empty;

        protected void UpdateHeaderInBuffer()
        {
            if (_bufferOwner == null || Header == null) return;
            Header.WriteTo(_bufferOwner.Memory.Span[..HeaderLength]);
        }

        public void Retain() => Interlocked.Increment(ref _refCount);

        public bool TryExtractBuffer(out IMemoryOwner<byte> bufferOwner, out int length)
        {
            if (_bufferOwner == null)
            {
                bufferOwner = null;
                length = 0;
                return false;
            }
            
            bufferOwner = Interlocked.Exchange(ref _bufferOwner, null);
            if (bufferOwner == null)
            {
                length = 0;
                return false;
            }

            length = TotalLength;
            return true;
        }
        
        public void Dispose()
        {
            if (Interlocked.Decrement(ref _refCount) != 0) return;
            _bufferOwner?.Dispose();
            _bufferOwner = null;
        }

        public void Serialize(
            IProtocolData protocol,
            IPolicy policy = null, 
            IEncryptor encryptor = null, 
            ICompressor compressor = null)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));
            
            var headerSize = Header.HeaderLength;
            var maxPayloadLength = policy?.MaxPayloadLength ?? LIMIT_PAYLOAD_LENGTH;
            
            if (policy is { UseCompress: true } && compressor != null)
                maxPayloadLength = compressor.GetMaxCompressedLength(maxPayloadLength);

            if (policy is { UseEncrypt: true } && encryptor != null)
                maxPayloadLength = encryptor.GetCiphertextLength(maxPayloadLength);

            var bufferCapacity = Math.Min(LIMIT_PAYLOAD_LENGTH, headerSize + maxPayloadLength);
            var bufferOwner = new PooledBuffer(bufferCapacity);
            
            var totalSpan = bufferOwner.Memory.Span;

            var span = totalSpan[headerSize..];
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
                
                Header = CreateHeader(flags, protocol.Id, written);
                Header.WriteTo(totalSpan[..headerSize]);
                
                _bufferOwner = bufferOwner;
                Retain();
            }
            catch
            {
                bufferOwner.Dispose();
                throw;
            }
        }

        public bool Deserialize<TProtocol>(TProtocol instance, IEncryptor encryptor, ICompressor compressor)
            where TProtocol : class, IProtocolData, new()
        {
            if (instance == null) return false;
            
            var srcSpan = PayloadSpan;
            if (srcSpan.IsEmpty) return false;

            var maxPlaintextLen = srcSpan.Length;
            if (HasFlag(HeaderFlags.Encrypted) && compressor != null)
                maxPlaintextLen = encryptor.GetPlaintextLength(maxPlaintextLen);

            if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                maxPlaintextLen = compressor.GetDecompressedLength(srcSpan);
            
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
                NetSerializer<TProtocol>.Deserialize(ref reader, instance);
                return true;
            }
            finally
            {
                owner.Dispose();
            }
        }

        private bool HasFlag(HeaderFlags flag) => Header != null && Header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort protocolId, int payloadLength);
    }
}
