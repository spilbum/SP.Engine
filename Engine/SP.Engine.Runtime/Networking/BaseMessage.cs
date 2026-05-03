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
        private IMemoryOwner<byte> _bodyOwner;
        protected THeader Header { get; set; }

        public int BodyLength { get; private set; }
        public ushort Id => Header?.Id ?? 0;
        
        protected BaseMessage()
        {
        }

        protected BaseMessage(THeader header, IMemoryOwner<byte> bodyOwner, int bodyLength)
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

        public void Serialize(IProtocolData protocol, IPolicy policy, IEncryptor encryptor, ICompressor compressor)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));

            var w = new NetWriter();
            protocol.Serialize(ref w);

            var bodySpan = w.WrittenSpan;
            var flags = HeaderFlags.None;

            IMemoryOwner<byte> tempOwner1 = null;
            IMemoryOwner<byte> tempOwner2 = null;

            try
            {
                if (policy.UseCompress && compressor != null && bodySpan.Length >= policy.CompressionThreshold)
                {
                    var maxLen = compressor.GetMaxCompressedLength(bodySpan.Length);
                    tempOwner1 = new PooledBuffer(maxLen);
                    var count = compressor.Compress(bodySpan, tempOwner1.Memory.Span);

                    bodySpan = tempOwner1.Memory.Span[..count];
                    flags |= HeaderFlags.Compressed;
                }

                if (policy.UseEncrypt && encryptor != null)
                {
                    var maxLen = encryptor.GetCiphertextLength(bodySpan.Length);
                    tempOwner2 = new PooledBuffer(maxLen);
                    var count = encryptor.Encrypt(bodySpan, tempOwner2.Memory.Span);

                    bodySpan = tempOwner2.Memory.Span[..count];
                    flags |= HeaderFlags.Encrypted;
                }
                
                if (tempOwner2 != null)
                {
                    _bodyOwner = (PooledBuffer)tempOwner2;
                    tempOwner2 = null;
                }
                else if (tempOwner1 != null)
                {
                    _bodyOwner = (PooledBuffer)tempOwner1;
                    tempOwner1 = null;
                }
                else
                {
                    var pooled = new PooledBuffer(bodySpan.Length);
                    bodySpan.CopyTo(pooled.GetSpan());
                    _bodyOwner = pooled;   
                }

                Retain();
                BodyLength = bodySpan.Length;
                Header = CreateHeader(flags, protocol.Id, bodySpan.Length);
            }
            finally
            {
                tempOwner1?.Dispose();
                tempOwner2?.Dispose();

                w.Dispose();
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
                var bodySpan = BodySpan;
                
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
                return NetSerializer.Deserialize<TProtocol>(ref reader);
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
