using System;
using System.Buffers.Binary;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;
using ArgumentNullException = System.ArgumentNullException;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : IMessage
    {
        public long SequenceNumber { get; private set; }
        public EProtocolId ProtocolId { get; private set; }

        private EMessageFlags _flags;
        private byte[] _payload;

        public void SetSequenceNumber(long sequenceNumber)
        {
            SequenceNumber = sequenceNumber;
        }

        public byte[] ToArray()
        {
            var payloadLength = (ushort)(_payload?.Length ?? 0);
            var totalLength = sizeof(long) + sizeof(ushort) + sizeof(byte) + sizeof(int) + payloadLength;
            var result = new byte[totalLength];
            var span = result.AsSpan();
            WriteHeader(span);

            var offset = sizeof(long) + sizeof(ushort) + sizeof(byte);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, sizeof(ushort)), payloadLength);
            offset += sizeof(int);

            if (payloadLength > 0)
                _payload.CopyTo(span.Slice(offset, payloadLength));

            return result;
        }

        private void WriteHeader(Span<byte> span)
        {
            var offset = 0;
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, sizeof(long)), SequenceNumber);
            offset += sizeof(long);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, sizeof(ushort)), (ushort)ProtocolId);
            offset += sizeof(ushort);
            span[offset] = (byte)_flags;
        }

        private static void ReadHeader(ReadOnlySpan<byte> span, out long sequence, out ushort protocolId,
            out byte flags)
        {
            var offset = 0;
            sequence = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, sizeof(long)));
            offset += sizeof(long);
            protocolId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            flags = span[offset];
        }

        public static TcpMessage TryReadBuffer(BinaryBuffer buffer, out int totalLength)
        {
            totalLength = 0;
            
            const int headerSize = sizeof(long) + sizeof(ushort) + sizeof(byte);
            const int minSize = headerSize + sizeof(int);
            if (buffer.RemainSize < minSize)
                return null;

            var span = buffer.Peek(minSize);
            ReadHeader(span, out var sequenceNumber, out var protocolId, out var messageFlags);
            var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(headerSize));

            var totalSize = minSize + payloadLength;
            if (buffer.RemainSize < totalSize)
                return null;

            buffer.Skip(headerSize);
            buffer.Skip(sizeof(int));
            var payload = buffer.ReadBytes(payloadLength);

            var message = new TcpMessage
            {
                SequenceNumber = sequenceNumber,
                ProtocolId = (EProtocolId)protocolId,
                _flags = (EMessageFlags)messageFlags,
                _payload = payload
            };

            totalLength = totalSize;
            return message;
        }

        public void SerializeProtocol(IProtocolData data, byte[] sharedKey = null)
        {
            ProtocolId = data.ProtocolId;
            _payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (_payload == null)
                throw new InvalidOperationException("Failed to serialize protocol");

            if (1024 < data.CompressibleSize && data.CompressibleSize < _payload.Length)
            {
                _flags |= EMessageFlags.Compressed;
                _payload = Compressor.Compress(_payload);
            }

            if (data.IsEncrypt)
            {
                if (sharedKey == null || sharedKey.Length == 0)
                    throw new ArgumentNullException(nameof(sharedKey));

                _flags |= EMessageFlags.Encrypted;
                _payload = Encryptor.Encrypt(_payload, sharedKey);
            }
        }

        public IProtocolData DeserializeProtocol(Type type, byte[] sharedKey = null)
        {
            if (_payload == null || _payload.Length == 0)
            {
                return null;
            }
        
            if (_flags.HasFlag(EMessageFlags.Encrypted))
            {
                if (sharedKey == null || sharedKey.Length == 0)
                    throw new ArgumentNullException(nameof(sharedKey));
        
                _payload = Encryptor.Decrypt(_payload, sharedKey);
            }
        
            if (_flags.HasFlag(EMessageFlags.Compressed))
            {
                _payload = Compressor.Decompress(_payload);
            }
        
            return BinaryConverter.DeserializeObject(_payload, type) as IProtocolData;
        }

        public override string ToString()
        {
            return $"[Seq:{SequenceNumber}] {ProtocolId} ({_flags}) Payload:{_payload?.Length ?? 0}B";
        }
    }
}
