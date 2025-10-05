using System;
using SP.Common.Buffer;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Networking
{
    internal static class SegmentUtil
    {
        public static byte[] ToArray(ArraySegment<byte> seg)
        {
            if (seg is { Array: { }, Offset: 0 } && seg.Count == seg.Array.Length)
                return seg.Array;
            var dst = new byte[seg.Count];
            if (seg.Count > 0)
                Buffer.BlockCopy(seg.Array!, seg.Offset, dst, 0, seg.Count);
            return dst;
        }
    }
    public abstract class BaseMessage<THeader>: IMessage
        where THeader : IHeader
    {
        protected THeader Header { get; set; }
        public ArraySegment<byte> Body { get; private set; }
        
        public ushort Id => Header.Id;
        public int Length => Header.Size + Body.Count;

        protected BaseMessage()
        {
            
        }
        
        protected BaseMessage(THeader header, ArraySegment<byte> body)
        {
            Header = header;
            Body = body;
        }

        private bool HasFlag(HeaderFlags flag)
            => Header != null && Header.Flags.HasFlag(flag);

        protected abstract THeader CreateHeader(HeaderFlags flags, ushort id, int payloadLength);

        public void Serialize(IProtocol protocol, IPolicy policy, IEncryptor encryptor, ICompressor compressor)
        {
            if (protocol is null) throw new ArgumentNullException(nameof(protocol));

            using var buf = new BinaryBuffer(1024);
            var w = new NetWriterBuffer(buf);
            BinaryConverter.Serialize(protocol, protocol.GetType(), w);
            var bodyBytes = buf.ToArray();
            
            var flags = HeaderFlags.None;
            if (policy.UseCompress && bodyBytes.Length >= policy.CompressionThreshold && compressor != null)
            {
                bodyBytes = compressor.Compress(bodyBytes);
                flags |= HeaderFlags.Compress;
            }

            if (policy.UseEncrypt && encryptor != null)
            {
                bodyBytes = encryptor.Encrypt(bodyBytes);
                flags |= HeaderFlags.Encrypt;
            }
            
            Header = CreateHeader(flags, protocol.Id, bodyBytes.Length);
            Body = new ArraySegment<byte>(bodyBytes);
        }

        public IProtocol Deserialize(Type type, IEncryptor encryptor, ICompressor compressor)
        {
            var payload = SegmentUtil.ToArray(Body);
            if (HasFlag(HeaderFlags.Encrypt))
            {
                if (encryptor == null) throw new InvalidOperationException("Encrypted payload but no decryptor provided");
                payload = encryptor.Decrypt(Body);
            }

            if (HasFlag(HeaderFlags.Compress))
            {
                if (compressor == null) throw new InvalidOperationException("Compressed payload but no compressor provided");
                payload = compressor.Decompress(payload);   
            }

            using var buf = new BinaryBuffer(payload.Length);
            buf.Write(payload);
            var r = new NetReaderBuffer(buf);
            var obj = BinaryConverter.Deserialize(r, type);
            return (IProtocol)obj;
        }

        public byte[] ToArray()
        {
            var bodyCount = Body.Count;
            using var buf = new BinaryBuffer(Header.Size + bodyCount);
            var w = new NetWriterBuffer(buf);
            Header.WriteTo(w);
            
            if (bodyCount <= 0 || Body.Array == null) 
                return buf.ToArray();
            
            var span = new ReadOnlySpan<byte>(Body.Array, Body.Offset, bodyCount);
            w.WriteBytes(span);
            return buf.ToArray();
        }
    }
}
