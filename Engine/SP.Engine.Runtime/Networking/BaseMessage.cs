using System;
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
        public ArraySegment<byte> Payload { get; private set; }
        
        public long SequenceNumber => Header.SequenceNumber;
        public ushort Id => Header.Id;
        public int Length => Payload.Count;

        protected BaseMessage()
        {
            
        }
        
        protected BaseMessage(THeader header, ArraySegment<byte> payload)
        {
            Header = header;
            Payload = payload;
            if (payload.Count < header.Length)
                throw new InvalidOperationException($"Invalid frame: payload({payload.Count}) < header({header.Length})");
        }

        private bool HasFlag(HeaderFlags flag)
            => Header != null && Header.Flags.HasFlag(flag);

        protected abstract THeader CreateHeader(ushort id, HeaderFlags flags, int payloadLength);

        protected ReadOnlySpan<byte> GetBodySpan()
            => new ReadOnlySpan<byte>(Payload.Array, Payload.Offset + Header.Length, Payload.Count - Header.Length);
        
        public void Serialize(IProtocol data, IPolicy policy, IEncryptor encryptor)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            
            var body = BinaryConverter.SerializeObject(data,  data.GetType());
            if (body == null || body.Length == 0)
                throw new InvalidOperationException($"Failed to serialize protocol of type {data.GetType().FullName}");

            var flags = HeaderFlags.None;
            
            if (policy.UseCompress && body.Length >= policy.CompressionThreshold)
            {
                body = Compressor.Compress(body);
                flags |= HeaderFlags.Compress;
            }

            if (policy.UseEncrypt)
            {
                if (encryptor == null)
                    throw new InvalidOperationException("Encryptor cannot be null when encryption is enabled.");

                body = encryptor.Encrypt(body);
                flags |= HeaderFlags.Encrypt;
            }

            var header = CreateHeader(data.Id, flags, body.Length);
            var frame = new byte[header.Length + body.Length];
            header.WriteTo(frame.AsSpan(0, header.Length));
            body.CopyTo(frame.AsSpan(header.Length, body.Length));
            
            Header = header;
            Payload = new ArraySegment<byte>(frame, 0, frame.Length);
        }

        public IProtocol Deserialize(Type type, IEncryptor encryptor)
        {
            var body = GetBodySpan();
            if (HasFlag(HeaderFlags.Encrypt))
            {
                if (encryptor == null)
                    throw new InvalidOperationException("Encryptor cannot be null.");
                body = encryptor.Decrypt(body);
            }

            if (HasFlag(HeaderFlags.Compress))
                body = Compressor.Decompress(body.ToArray());
            
            var obj = BinaryConverter.DeserializeObject(body.ToArray(), type);
            if (obj is IProtocol p) return p;

            throw new InvalidCastException($"Deserialized object is not IProtocol: {type.FullName}");
        }
    }
}
