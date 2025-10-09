using System;
using System.IO;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Networking
{
    public abstract class BaseMessage<THeader>: IMessage
        where THeader : IHeader
    {
        protected THeader Header { get; set; }
        protected ArraySegment<byte> Body { get; private set; }
        
        public ushort Id => Header.Id;

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

            var w = new NetWriter();
            BinaryConverter.Serialize(ref w, protocol.GetType(), protocol);
            var payload = w.ToArray();
            
            var flags = HeaderFlags.None;
            if (policy.UseCompress && payload.Length >= policy.CompressionThreshold && compressor != null)
            {
                payload = compressor.Compress(payload);
                flags |= HeaderFlags.Compressed;
            }

            if (policy.UseEncrypt && encryptor != null)
            {
                payload = encryptor.Encrypt(payload);
                flags |= HeaderFlags.Encrypted;
            }
            
            Header = CreateHeader(flags, protocol.Id, payload.Length);
            Body = new ArraySegment<byte>(payload);
        }

        public IProtocol Deserialize(Type type, IEncryptor encryptor, ICompressor compressor)
        {
            var payload = Body.ToArray();
            
            if (HasFlag(HeaderFlags.Compressed) && compressor == null)
                throw new InvalidDataException("Compressed payload but no compressor provided.");
            if (HasFlag(HeaderFlags.Encrypted) && encryptor == null)
                throw new InvalidDataException("Encrypted payload but no decryptor provided.");

            if (HasFlag(HeaderFlags.Encrypted))
                payload = encryptor.Decrypt(payload);
            
            if (HasFlag(HeaderFlags.Compressed)) 
                payload = compressor.Decompress(payload);

            var r = new NetReader(payload);
            var obj = BinaryConverter.Deserialize(ref r, type);
            return (IProtocol)obj;
        }

        public ArraySegment<byte> ToArray()
        {
            var bodyLen = Body.Count;
            var buf = new byte[Header.Size + bodyLen];
            var span = buf.AsSpan();
            Header.WriteTo(span);
            
            if (bodyLen > 0 && Body.Array != null)
                Buffer.BlockCopy(Body.Array, Body.Offset, buf, Header.Size, bodyLen);
            
            return new ArraySegment<byte>(buf);
        }
    }
}
