using System;
using System.Buffers;
using System.Threading;
using SP.Core.Buffers;
using SP.Core.Serialization;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Policy;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public abstract class MessageBase<THeader, TMessage> : IMessage
        where THeader : IHeader
        where TMessage : MessageBase<THeader, TMessage>, new()
    {
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

        private ReadOnlySpan<byte> PayloadSpan => _bufferOwner != null
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
            
            using var resizerA = BufferResizer.Rent();
            using var resizerB = BufferResizer.Rent();
            
            var writer = new NetWriter(resizerA.Span, resizerA);
            protocol.Serialize(ref writer);
            
            var currentSpan = resizerA.GetWrittenSpan(writer.WrittenCount);
            
            var doCompress = policy is { UseCompress: true } && compressor != null && currentSpan.Length >= policy.CompressionThreshold;
            var doEncrypt = policy is { UseEncrypt: true } && encryptor != null;
            var flags = HeaderFlags.None;

            if (doCompress)
            {
                var maxCompressedLength = compressor.GetMaxCompressedLength(currentSpan.Length);
                resizerB.Resize(maxCompressedLength, 0);
                
                var compressedLength = compressor.Compress(currentSpan, resizerB.Span);
                currentSpan = resizerB.GetWrittenSpan(compressedLength);
                flags |= HeaderFlags.Compressed;
            }

            if (doEncrypt)
            {
                var maxEncryptedLength = encryptor.GetCiphertextLength(currentSpan.Length);
                Span<byte> targetSpan;

                if (doCompress)
                {
                    resizerA.Resize(maxEncryptedLength, 0);
                    targetSpan = resizerA.Span;
                }
                else
                {
                    resizerB.Resize(maxEncryptedLength, 0);
                    targetSpan = resizerB.Span;
                }
                
                var encryptedLength = encryptor.Encrypt(currentSpan, targetSpan);
                currentSpan = targetSpan[..encryptedLength];
                flags |= HeaderFlags.Encrypted;
            }
            
            var bufferOwner = BufferOwnerPool.Rent(headerSize + currentSpan.Length);

            try
            {
                var totalSpan = bufferOwner.Memory.Span;
                currentSpan.CopyTo(totalSpan[headerSize..]);

                _header = CreateHeader(flags, protocol.Id, currentSpan.Length);
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
            
            var currentSpan = PayloadSpan;
            if (currentSpan.IsEmpty) return;
            
            var isEncrypted = HasFlag(HeaderFlags.Encrypted) && encryptor != null;
            var isCompressed = HasFlag(HeaderFlags.Compressed) && compressor != null;

            if (!isEncrypted && !isCompressed)
            {
                var reader = new NetReader(currentSpan);
                protocol.Deserialize(ref reader);
                return;
            }

            BufferResizer resizeA = null;
            BufferResizer resizeB = null;

            try
            {
                if (isEncrypted)
                {
                    var plainLength = encryptor.GetPlaintextLength(currentSpan.Length);
                    resizeA = BufferResizer.Rent(plainLength);
                    
                    var written = encryptor.Decrypt(currentSpan, resizeA.Span);
                    currentSpan = resizeA.GetWrittenSpan(written);
                }

                if (isCompressed)
                {
                    var decompressedLength = compressor.GetDecompressedLength(currentSpan);
                    
                    var targetResizer = isEncrypted
                        ? resizeB = BufferResizer.Rent(decompressedLength)
                        : resizeA = BufferResizer.Rent(decompressedLength);
                    
                    var written = compressor.Decompress(currentSpan, targetResizer.Span);
                    currentSpan = targetResizer.GetWrittenSpan(written);
                }

                var reader = new NetReader(currentSpan);
                protocol.Deserialize(ref reader);
            }
            finally
            {
                resizeA?.Dispose();
                resizeB?.Dispose();
            }
        }

        private bool HasFlag(HeaderFlags flag) => _header != null && _header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort protocolId, int payloadLength);
    }
}
