using System;
using System.Buffers;
using System.Threading;
using SP.Core.Buffers;
using SP.Core.Serialization;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public abstract class MessageBase<THeader, TMessage> : IMessage
        where THeader : IHeader
        where TMessage : MessageBase<THeader, TMessage>, new()
    {
        private const int LIMIT_PAYLOAD_LENGTH = 64 * 1024;
        private IMemoryOwner<byte> _bufferOwner;
        protected THeader _header;
        
        public ushort Id => _header?.ProtocolId ?? 0;
        public int PayloadLength => _header?.PayloadLength ?? 0;
        public int TotalLength => HeaderLength + PayloadLength;
        public bool IsEmpty => Volatile.Read(ref _bufferOwner) == null;
        
        protected abstract int HeaderLength { get; }

        public void Initialize(THeader header, IMemoryOwner<byte> bufferOwner)
        {
            _header = header;
            _bufferOwner = bufferOwner;
        }

        private Span<byte> PayloadSpan => _bufferOwner != null
            ? _bufferOwner.Memory.Span.Slice(HeaderLength, PayloadLength)
            : Span<byte>.Empty;

        protected void UpdateHeaderInBuffer()
        {
            if (_bufferOwner == null || _header == null) return;
            _header.WriteTo(_bufferOwner.Memory.Span[..HeaderLength]);
        }

        public TMessage Extract()
        {
            var owner = Interlocked.Exchange(ref _bufferOwner, null);
            if (owner == null)
                throw new ObjectDisposedException(nameof(MessageBase<THeader, TMessage>),
                    "Cannot extract an empty or disposed message.");
            
            var message = MessagePool<TMessage>.Rent();
            message.Initialize(_header, owner);
            return message;
        }

        public TMessage Clone()
        {
            var owner = Volatile.Read(ref _bufferOwner);
            if (owner == null)
                throw new ObjectDisposedException(nameof(MessageBase<THeader, TMessage>),
                    "Cannot clone an empty or disposed message.");

            var newOwner = BufferOwnerPool.Rent(TotalLength);
            owner.Memory.Span[..TotalLength].CopyTo(newOwner.Memory.Span);
            
            var message = MessagePool<TMessage>.Rent();
            message.Initialize(_header, newOwner);
            return message;
        }
        
        public bool TryGetBuffer(out ReadOnlyMemory<byte> memory)
        {
            var owner = Volatile.Read(ref _bufferOwner);
            if (owner == null)
            {
                memory = default;
                return false;
            }

            try
            {
                memory = _bufferOwner.Memory[..TotalLength];
                return true;
            }
            catch (ObjectDisposedException)
            {
                memory = default;
                return false;
            }
        }
        
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _bufferOwner, null);
            owner?.Dispose();

            _header = default;

            if (this is TMessage message)
            {
                MessagePool<TMessage>.Return(message);
            }
        }

        public void Serialize(
            IProtocolData protocol,
            IPolicy policy = null, 
            IEncryptor encryptor = null, 
            ICompressor compressor = null)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));
            
            var headerSize = _header.HeaderLength;
            var maxPayloadLength = policy?.MaxPayloadLength ?? LIMIT_PAYLOAD_LENGTH;
            
            if (policy is { UseCompress: true } && compressor != null)
                maxPayloadLength = compressor.GetMaxCompressedLength(maxPayloadLength);

            if (policy is { UseEncrypt: true } && encryptor != null)
                maxPayloadLength = encryptor.GetCiphertextLength(maxPayloadLength);

            var bufferCapacity = Math.Min(LIMIT_PAYLOAD_LENGTH, headerSize + maxPayloadLength);
            var bufferOwner = BufferOwnerPool.Rent(bufferCapacity);
            
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
                
                _header = CreateHeader(flags, protocol.Id, written);
                _header.WriteTo(totalSpan[..headerSize]);
                _bufferOwner = bufferOwner;
            }
            catch
            {
                bufferOwner.Dispose();
                throw;
            }
        }

        public void Deserialize<TProtocol>(TProtocol protocol, IEncryptor encryptor, ICompressor compressor)
            where TProtocol : class, IProtocolData, new()
        {
            if (protocol == null) throw new ArgumentNullException(nameof(protocol));
            
            var sourceSpan = PayloadSpan;
            if (sourceSpan.IsEmpty) return;

            var maxPlaintextLen = sourceSpan.Length;
            if (HasFlag(HeaderFlags.Encrypted) && compressor != null)
                maxPlaintextLen = encryptor.GetPlaintextLength(maxPlaintextLen);

            if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                maxPlaintextLen = compressor.GetDecompressedLength(sourceSpan);
            
            var bufferOwner = BufferOwnerPool.Rent(maxPlaintextLen);
            var span = bufferOwner.Memory.Span;

            try
            {
                if (HasFlag(HeaderFlags.Encrypted) && encryptor != null)
                {
                    var written = encryptor.Decrypt(sourceSpan, span);
                    sourceSpan = span[..written];
                }

                if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                {
                    var destSpan = span[sourceSpan.Length..];
                    var written = compressor.Decompress(sourceSpan, destSpan);
                    sourceSpan = destSpan[..written];
                }

                var reader = new NetReader(sourceSpan);
                protocol.Deserialize(ref reader);
            }
            finally
            {
                bufferOwner.Dispose();
            }
        }

        private bool HasFlag(HeaderFlags flag) => _header != null && _header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort protocolId, int payloadLength);
    }
}
