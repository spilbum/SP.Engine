using System;
using System.Collections.Generic;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Message
{
    public class UdpMessage : IMessage
    {
        private UdpHeader _header;
        private ArraySegment<byte> _payload;

        public long SequenceNumber => _header.SequenceNumber;
        public EProtocolId ProtocolId => _header.ProtocolId;
        public int Length => _payload.Count;
        public UdpHeader Header => _header;
        public ArraySegment<byte> Payload => _payload;
        private bool IsEncrypted => _header.Flags.HasFlag(EHeaderFlags.Encrypted);
        private bool IsCompressed => _header.Flags.HasFlag(EHeaderFlags.Compressed);
        private byte[] Body => _payload.AsSpan(UdpHeader.HeaderSize, _payload.Count - UdpHeader.HeaderSize).ToArray();

        public UdpMessage()
        {
            
        }
        
        public UdpMessage(UdpHeader header, ArraySegment<byte> payload)
        {
            _header = header;
            _payload = payload;
        }

        public void SetSequenceNumber(long sequenceNumber)
        {
            _header = new UdpHeaderBuilder()
                .From(_header)
                .WithSequenceNumber(sequenceNumber)
                .Build();
        }

        public void SetPeerId(EPeerId peerId)
        {
            _header = new UdpHeaderBuilder()
                .From(_header)
                .WithPeerId(peerId)
                .Build();
        }

        public void Pack(IProtocolData data, byte[] sharedKey, PackOptions options)
        {
            var body = BinaryConverter.SerializeObject(data, data.GetType());
            if (body == null || body.Length == 0)
                throw new InvalidOperationException($"Failed to deserialize message: {data.ProtocolId}");

            var flags = EHeaderFlags.None;
            if (options?.UseCompression ?? false)
            {
                var compressed = Compressor.Compress(body);
                var ratio = (double)compressed.Length / body.Length;
                if (ratio < options.CompressionThreshold)
                {
                    body = compressed;
                    flags |= EHeaderFlags.Compressed;
                }
            }

            if (options?.UseEncryption ?? false)
            {
                if (sharedKey == null)
                    throw new InvalidOperationException("SharedKey cannot be null when encryption is enabled.");
                body = Encryptor.Encrypt(body, sharedKey);
                flags |= EHeaderFlags.Encrypted;
            }
            
            var header = new UdpHeaderBuilder()
                .From(_header)
                .WithProtocolId(data.ProtocolId)
                .WithPayloadLength(body.Length)
                .AddFlag(flags)
                .Build();
            
            _header = header;
            
            var buffer = new byte[UdpHeader.HeaderSize + body.Length];
            header.WriteTo(buffer.AsSpan(0, UdpHeader.HeaderSize));
            body.CopyTo(buffer.AsSpan(UdpHeader.HeaderSize));
            
            _payload = new ArraySegment<byte>(buffer, 0, buffer.Length);
        }

        public IProtocolData Unpack(Type type, byte[] sharedKey = null)
        {
            var body = Body;
            if (IsEncrypted)
            {
                if (sharedKey == null)
                    throw new InvalidOperationException("SharedKey cannot be null.");
                body = Encryptor.Decrypt(body, sharedKey);
            }

            if (IsCompressed)
                body = Compressor.Decompress(body);
            return BinaryConverter.DeserializeObject(body, type) as IProtocolData;
        }

        public void WriteTo(Span<byte> buffer)
        {
            if (buffer.Length < _payload.Count)
                throw new ArgumentException("Buffer too small.", nameof(buffer));
            _payload.AsSpan().CopyTo(buffer);
        }

        public IEnumerable<UdpFragment> ToSplit(ushort mtu)
        {
            if (mtu < 256)
                throw new ArgumentOutOfRangeException(nameof(mtu), "MTU must be at least 256");
            
            var body = Body;
            if (body.Length <= mtu)
                yield break;
            
            var header = new UdpHeaderBuilder()
                .From(_header)
                .AddFlag(EHeaderFlags.Fragmentation)
                .Build();
      
            var totalCount = (byte)Math.Ceiling(body.Length / (float)mtu);
            for (byte i = 0; i < totalCount; i++)
            {
                var offset = i * mtu;
                var size = (ushort)Math.Min(mtu, body.Length - offset);
                var fragHeader = new UdpFragment.Header(i, totalCount, size);
                var fragPayload = new ArraySegment<byte>(body, offset, size);
                yield return new UdpFragment(header, fragHeader, fragPayload);
            }
        }
    }
}

