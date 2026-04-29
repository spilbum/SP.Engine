using System;
using System.Buffers;
using System.Threading;
using SP.Core;
using SP.Core.Serialization;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public abstract class BaseMessage<THeader> : IMessage where THeader : IHeader
    {
        private int _refCount;
        protected THeader Header { get; set; }
        protected IMemoryOwner<byte> _bodyOwner;
        
        public int BodyLength => _bodyOwner?.Memory.Length ?? 0;
        public ushort Id => Header?.Id ?? 0;
        
        protected BaseMessage()
        {
        }

        protected BaseMessage(THeader header, IMemoryOwner<byte> bodyOwner)
        {
            Header = header;
            _bodyOwner = bodyOwner;
            Retain();
        }

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
        
        public void Serialize(IProtocolData protocol, IPolicy policy, IEncryptor encryptor, ICompressor compressor)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));

            using var w = new NetWriter();
            NetSerializer.Serialize(w, protocol.GetType(), protocol);
            
            var currentSpan = w.WrittenSpan;
            var flags = HeaderFlags.None;

            IMemoryOwner<byte> tempOwner1 = null;
            IMemoryOwner<byte> tempOwner2 = null;
            
            try
            {
                if (policy.UseCompress && compressor != null && currentSpan.Length >= policy.CompressionThreshold)
                {
                    var maxLen = compressor.GetMaxCompressedLength(currentSpan.Length);
                    tempOwner1 = new PooledBuffer(maxLen);
                    var count = compressor.Compress(currentSpan, tempOwner1.Memory.Span);

                    currentSpan = tempOwner1.Memory.Span[..count];
                    flags |= HeaderFlags.Compressed;
                }

                if (policy.UseEncrypt && encryptor != null)
                {
                    var maxLen = encryptor.GetCiphertextLength(currentSpan.Length);
                    tempOwner2 = new PooledBuffer(maxLen);
                    var count = encryptor.Encrypt(currentSpan, tempOwner2.Memory.Span);
                    
                    currentSpan = tempOwner2.Memory.Span[..count];
                    flags |= HeaderFlags.Encrypted;
                }

                var finalOwner = new PooledBuffer(currentSpan.Length);
                currentSpan.CopyTo(finalOwner.Span);
                
                Retain();
                _bodyOwner = finalOwner;
                Header = CreateHeader(flags, protocol.Id, currentSpan.Length);
            }
            finally
            {
                tempOwner1?.Dispose();
                tempOwner2?.Dispose();
            }
        }

        public TProtocol Deserialize<TProtocol>(IEncryptor encryptor, ICompressor compressor)
            where TProtocol : IProtocolData
        {
            if (_bodyOwner == null)
            {
                return default;
            }
            
            IMemoryOwner<byte> tempOwner1 = null;
            IMemoryOwner<byte> tempOwner2 = null;
            
            try
            {
                var bodySpan = _bodyOwner.Memory.Span;
                if (HasFlag(HeaderFlags.Encrypted) && encryptor != null)
                {
                    var maxLen = encryptor.GetPlaintextLength(bodySpan.Length);
                    tempOwner1 = new PooledBuffer(maxLen);
                    
                    var count = encryptor.Decrypt(bodySpan, tempOwner1.Memory.Span);
                    bodySpan = tempOwner1.Memory.Span[..count];
                }

                if (HasFlag(HeaderFlags.Compressed) && compressor != null)
                {
                    var decompressedLength = compressor.GetDecompressedLength(bodySpan);
                    tempOwner2 = new PooledBuffer(decompressedLength);
                    
                    var count = compressor.Decompress(bodySpan, tempOwner2.Memory.Span);
                    bodySpan = tempOwner2.Memory.Span[..count];
                }
                
                var reader = new NetReader(bodySpan);
                return (TProtocol)NetSerializer.Deserialize(ref reader, typeof(TProtocol));
            }
            finally
            {
               tempOwner1?.Dispose();
               tempOwner2?.Dispose();
            }
        }

        private bool HasFlag(HeaderFlags flag) => Header != null && Header.HasFlag(flag);
        protected abstract THeader CreateHeader(HeaderFlags flags, ushort protocolId, int bodyLength);
    }
}
