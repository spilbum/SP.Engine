using System;
using System.Buffers.Binary;
using SP.Engine.Core.Compression;
using SP.Engine.Core.Networking.Buffers;
using SP.Engine.Core.Protocols;
using SP.Engine.Core.Security;
using SP.Engine.Core.Serialization;

namespace SP.Engine.Core.Networking
{
    public class TcpMessage : IMessage
    {
        public long SequenceNumber { get; private set; }
        public EProtocolId ProtocolId { get; private set; }
        public EMessageFlags Flags => _flags;

        private EMessageFlags _flags;
        private byte[] _payload;

        public void SetSequenceNumber(long sequenceNumber)
        {
            SequenceNumber = sequenceNumber;
        }

        public byte[] ToArray()
        {
            var payloadLength = _payload?.Length ?? 0;
            var totalLength = sizeof(long) + sizeof(ushort) + sizeof(byte) + sizeof(int) + payloadLength;
            var result = new byte[totalLength];
            var span = result.AsSpan();
            WriteHeader(span);

            var offset = sizeof(long) + sizeof(ushort) + sizeof(byte);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, sizeof(int)), payloadLength);
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
            int offset = 0;
            sequence = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, sizeof(long)));
            offset += sizeof(long);
            protocolId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, sizeof(ushort)));
            offset += sizeof(ushort);
            flags = span[offset];
        }

        public static bool TryReadBuffer(BinaryBuffer buffer, out TcpMessage message)
        {
            message = null;

            var headerSize = sizeof(long) + sizeof(ushort) + sizeof(byte);
            var minSize = headerSize + sizeof(int);
            if (buffer.RemainSize < minSize)
                return false;

            var span = buffer.Peek(minSize);
            ReadHeader(span, out var sequence, out var proto, out var flag);
            var payloadSize = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(headerSize));

            var totalSize = minSize + payloadSize;
            if (buffer.RemainSize < totalSize)
                return false;

            buffer.Skip(headerSize);
            buffer.Skip(sizeof(int));
            var payload = buffer.ReadBytes(payloadSize);

            message = new TcpMessage
            {
                SequenceNumber = sequence,
                ProtocolId = (EProtocolId)proto,
                _flags = (EMessageFlags)flag,
                _payload = payload
            };

            return true;
        }

        public void SerializeProtocol(IProtocolData data, byte[] sharedKey)
        {
            ProtocolId = data.ProtocolId;
            _payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (_payload == null)
                throw new InvalidOperationException("Failed to serialize protocol");

            if (data.CompressibleSize < _payload.Length)
            {
                _flags |= EMessageFlags.Compressed;
                _payload = Compressor.Compress(_payload);
            }

            if (data.IsEncrypt)
            {
                if (sharedKey == null || sharedKey.Length == 0)
                    throw new ArgumentException(nameof(sharedKey));

                _flags |= EMessageFlags.Encrypted;
                _payload = Encryptor.Encrypt(sharedKey, _payload);
            }
        }

        public IProtocolData DeserializeProtocol(Type type, byte[] sharedKey)
        {
            if (_payload == null || _payload.Length == 0)
            {
                return null;
            }

            if (_flags.HasFlag(EMessageFlags.Encrypted))
            {
                if (sharedKey == null || sharedKey.Length == 0)
                    throw new ArgumentException(nameof(sharedKey));

                _payload = Encryptor.Decrypt(sharedKey, _payload);
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
