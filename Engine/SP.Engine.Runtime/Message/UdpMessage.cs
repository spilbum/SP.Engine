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
        private ArraySegment<byte> _buffer;

        public long SequenceNumber => _header.SequenceNumber;
        public EProtocolId ProtocolId => _header.ProtocolId;
        public int Length => _buffer.Count;
        public UdpHeader Header => _header;
        public ArraySegment<byte> Payload => _buffer;
        private bool IsEncrypted => _header.Flags.HasFlag(EHeaderFlags.Encrypted);
        private bool IsCompressed => _header.Flags.HasFlag(EHeaderFlags.Compressed);

        private byte[] GetBody()
            => _buffer.AsSpan(UdpHeader.HeaderSize, _buffer.Count - UdpHeader.HeaderSize).ToArray();
        
        public UdpMessage()
        {
            
        }
        
        public UdpMessage(UdpHeader header, ArraySegment<byte> payload)
        {
            _header = header;
            _buffer = payload;
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
            _buffer = new ArraySegment<byte>(buffer, 0, buffer.Length);
        }

        public IProtocolData Unpack(Type type, byte[] sharedKey)
        {
            var body = GetBody();
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
            if (buffer.Length > _buffer.Count)
                throw new ArgumentException("Buffer too small.", nameof(buffer));
            _buffer.AsSpan().CopyTo(buffer);
        }

        public IEnumerable<UdpFragment> ToSplit(ushort mtu, uint fragmentId)
        {
            const int overhead = UdpHeader.HeaderSize + UdpFragmentHeader.HeaderSize;
            if (mtu <= overhead)
                throw new ArgumentOutOfRangeException(nameof(mtu), $"mtu({mtu}) <= overhead({overhead})");

            var maxSize = mtu - overhead;
            var body = GetBody();
            
            var totalCount = (byte)Math.Ceiling(body.Length / (float)maxSize);
            var header = new UdpHeaderBuilder()
                .From(_header)
                .AddFlag(EHeaderFlags.Fragmentation)
                .Build();
      
            for (byte i = 0; i < totalCount; i++)
            {
                var offset = i * maxSize;
                var size = (ushort)Math.Min(maxSize, body.Length - offset);
                yield return new UdpFragment(
                    header,
                    new UdpFragmentHeader(fragmentId, i, totalCount, size),
                    new ArraySegment<byte>(body, offset, size));
            }
        }
    }
}

