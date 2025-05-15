using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SP.Common.Buffer;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Runtime.Serialization;
using ArgumentNullException = System.ArgumentNullException;

namespace SP.Engine.Runtime.Networking
{
    public class TcpMessage : IMessage
    {
        private static class HeaderLayout
        {
            public const int SequenceOffset = 0;
            public const int SequenceSize = sizeof(long);

            public const int ProtocolOffset = SequenceOffset + SequenceSize;
            public const int ProtocolSize = sizeof(ushort);

            public const int FlagsOffset = ProtocolOffset + ProtocolSize;
            public const int FlagsSize = sizeof(byte);

            public const int PayloadLengthOffset = FlagsOffset + FlagsSize;
            public const int PayloadLengthSize = sizeof(ushort);

            public const int TotalHeaderSize = PayloadLengthOffset + PayloadLengthSize;
        }

        public static TcpMessage Create(IProtocolData data, DiffieHellman dh = null)
        {
            var key = data.ProtocolId.IsEngineProtocol() ? null : dh?.SharedKey;
            var message = new TcpMessage();
            message.SerializeProtocol(data, key);
            return message;
        }

        private const double CompressionThreshold = 0.9;
        
        public long SequenceNumber { get; private set; }
        public EProtocolId ProtocolId { get; private set; }
        public bool IsEncypted => _flags.HasFlag(EMessageFlags.Encrypted);
        public bool IsCompressed => _flags.HasFlag(EMessageFlags.Compressed);

        private EMessageFlags _flags;
        private byte[] _payload;

        public void SetSequenceNumber(long sequenceNumber)
        {
            SequenceNumber = sequenceNumber;
        }

        public byte[] ToArray()
        {
            var payloadLength = (ushort)(_payload?.Length ?? 0);
            var totalLength = HeaderLayout.TotalHeaderSize + payloadLength;
            var result = new byte[totalLength];
            var span = result.AsSpan();
            WriteHeader(span);
            
            if (payloadLength > 0)
                _payload.CopyTo(span.Slice(HeaderLayout.TotalHeaderSize, payloadLength));

            return result;
        }

        private void WriteHeader(Span<byte> span)
        {
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(HeaderLayout.SequenceOffset, sizeof(long)), SequenceNumber);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(HeaderLayout.ProtocolOffset, sizeof(ushort)), (ushort)ProtocolId);
            span[HeaderLayout.FlagsOffset] = (byte)_flags;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(HeaderLayout.PayloadLengthOffset, sizeof(ushort)), (ushort)(_payload?.Length ?? 0));
        }

        private static bool TryReadHeader(ReadOnlySpan<byte> span, out long sequenceNumber, out ushort protocolId, out byte flags, out ushort payloadLength)
        {
            sequenceNumber = 0;
            protocolId = 0;
            flags = 0;
            payloadLength = 0;
            
            if (span.Length < HeaderLayout.TotalHeaderSize)
                return false;
            
            sequenceNumber = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(HeaderLayout.SequenceOffset, sizeof(long)));
            protocolId = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(HeaderLayout.ProtocolOffset, sizeof(ushort)));
            flags = span[HeaderLayout.FlagsOffset];
            payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(HeaderLayout.PayloadLengthOffset, sizeof(ushort)));
            return true;
        }

        public static TcpMessage TryReadBuffer(BinaryBuffer buffer, out int totalLength)
        {
            totalLength = 0;
            
            if (buffer.RemainSize < HeaderLayout.TotalHeaderSize)
                return null;

            var span = buffer.Peek(HeaderLayout.TotalHeaderSize);
            if (!TryReadHeader(span, out var sequenceNumber, out var protocolId, out var flags, out var payloadLength)) 
                return null;
            
            var totalSize = HeaderLayout.TotalHeaderSize + payloadLength;
            if (buffer.RemainSize < totalSize)
                return null;

            buffer.Skip(HeaderLayout.TotalHeaderSize);
            var payload = buffer.ReadBytes(payloadLength);
            totalLength = totalSize;
            
            return new TcpMessage
            {
                SequenceNumber = sequenceNumber,
                ProtocolId = (EProtocolId)protocolId,
                _flags = (EMessageFlags)flags,
                _payload = payload
            };
        }

        public void SerializeProtocol(IProtocolData data, byte[] sharedKey = null)
        {
            ProtocolId = data.ProtocolId;
            _payload = SerializePayload(data, sharedKey);
        }
        
        private byte[] SerializePayload(IProtocolData data, byte[] sharedKey)
        {
            var payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (payload == null)
                throw new InvalidOperationException("Failed to serialize protocol");

            var compressed = Compressor.Compress(payload);
            var ratio = (double)compressed.Length / payload.Length;
            if (ratio < CompressionThreshold)
            {
                _flags |= EMessageFlags.Compressed;
                payload = compressed;
            }

            if (!data.IsEncrypt) return payload;
            if (sharedKey == null || sharedKey.Length == 0)
                throw new ArgumentNullException(nameof(sharedKey), "Encrytption required but sharedKey is null");

            _flags |= EMessageFlags.Encrypted;
            return Encryptor.Encrypt(payload, sharedKey);
        }

        public IProtocolData DeserializeProtocol(Type type, byte[] sharedKey = null)
        {
            if (_payload == null || _payload.Length == 0)
                return null;
            var raw = DeserializePayload(_payload, sharedKey);
            return BinaryConverter.DeserializeObject(raw, type) as IProtocolData;
        }

        private byte[] DeserializePayload(byte[] input, byte[] sharedKey)
        {
            var result = input;
            
            if (IsEncypted)
            {
                if (sharedKey == null || sharedKey.Length == 0)
                    throw new ArgumentNullException(nameof(sharedKey), "Encrytption required but sharedKey is null");
                result = Encryptor.Decrypt(result, sharedKey);
            }
        
            if (IsCompressed)
            {
                result = Compressor.Decompress(result);
            }
        
            return result;
        }

        public override string ToString()
        {
            var info = new List<string>
            {
                $"Seq:{SequenceNumber}",
                $"Protocol:{ProtocolId}",
                $"Type:{(ProtocolId.IsEngineProtocol() ? "Engine" : "Server")}"
            };

            if (_flags.HasFlag(EMessageFlags.Compressed))
                info.Add("Compressed");

            if (_flags.HasFlag(EMessageFlags.Encrypted))
                info.Add("Encrypted");

            info.Add($"Size:{_payload?.Length ?? 0}B");

            return "[TcpMessage] " + string.Join(" | ", info);
        }
    }
}
