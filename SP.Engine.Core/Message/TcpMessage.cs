using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SP.Engine.Core.Protocol;
using SP.Engine.Core.Utility;
using SP.Engine.Core.Utility.Crypto;

namespace SP.Engine.Core.Message
{
    public class TcpMessage : IMessage
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Header
        {
            public long SequenceNumber;
            public ushort ProtocolId;
            public byte Flags;

            public static int Size => Marshal.SizeOf<Header>();
            
            public void AddFlags(EMessageFlags toAdd) 
                => Flags |= (byte)toAdd;
        
            public void RemoveFlags(EMessageFlags toRemove) 
                => Flags &= (byte)~toRemove;

            public bool HasFlags(EMessageFlags check)
                => (Flags & (byte)check) != 0;
            
            public override string ToString()
                => $"Seq:{SequenceNumber} Proto:{(EProtocolId)ProtocolId} Flags:{(EMessageFlags)Flags}";
        }
        
        private static void SerializeHeader(Header header, Span<byte> buffer)
        {
            Debug.Assert(buffer.Length >= Header.Size);
            MemoryMarshal.Write(buffer, ref header);
        }

        private static Header DeserializeHeader(ReadOnlySpan<byte> data)
        {
            if (data.Length < Header.Size)
                throw new ArgumentException("Insufficient data to deserialize header", nameof(data));

            return MemoryMarshal.Read<Header>(data);
        }
        
        public long SequenceNumber => _header.SequenceNumber;
        public EProtocolId ProtocolId => (EProtocolId)_header.ProtocolId;

        private Header _header;
        private byte[] _payload;
        
        public void SetSequenceNumber(long sequenceNumber)
        {
            _header.SequenceNumber = sequenceNumber;
        }

        public byte[] ToArray()
        {
            var payloadLength = _payload?.Length ?? 0;
            var totalLength = Header.Size + sizeof(int) + payloadLength;

            var result = new byte[totalLength];
            var span = result.AsSpan();
            var offset = 0;

            // Serialize Header
            SerializeHeader(_header, span.Slice(offset, Header.Size));
            offset += Header.Size;

            // Write Payload Size (Little Endian)
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, sizeof(int)), payloadLength);
            offset += sizeof(int);

            // Copy Payload
            if (payloadLength > 0)
                _payload.CopyTo(span.Slice(offset, payloadLength));

            return result;
        }
        
        public static bool TryReadBuffer(Buffer buffer, out TcpMessage message)
        {
            message = null;

            var minSize = Header.Size + sizeof(int); // Header + PayloadSize
            if (buffer.RemainSize < minSize)
                return false;

            var headerAndLength = buffer.Peek(minSize);
            var header = DeserializeHeader(headerAndLength.Slice(0, Header.Size));
            var payloadSize = BinaryPrimitives.ReadInt32LittleEndian(headerAndLength.Slice(Header.Size));

            var totalSize = minSize + payloadSize;
            if (buffer.RemainSize < totalSize)
                return false;

            // 실제 데이터 Read
            var headerSpan = buffer.Read(Header.Size);
            var length = buffer.Read<int>();
            var payload = buffer.ReadBytes(length);

            message = new TcpMessage { _header = DeserializeHeader(headerSpan), _payload = payload };
            return true;
        }
 
        public void SerializeProtocol(IProtocolData data, byte[] sharedKey)
        {
            _header.ProtocolId = (ushort)data.ProtocolId;
            _payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (null == _payload)
                throw new InvalidOperationException("Failed to serialize protocol");

            if (1024 < data.CompressibleSize && data.CompressibleSize <= _payload.Length)
            {
                _header.AddFlags(EMessageFlags.Compressed);
                _payload = Compressor.Compress(_payload);
            }

            if (!data.IsEncrypt) return;
            if (null == sharedKey || 0 == sharedKey.Length)
                throw new ArgumentException(nameof(sharedKey));

            _header.AddFlags(EMessageFlags.Encrypted);
            _payload = Encryptor.Encrypt(sharedKey, _payload);
        }

        public IProtocolData DeserializeProtocol(Type type, byte[] sharedKey)
        {
            if (null == _payload || 0 == _payload.Length)
                return null;

            if (_header.HasFlags(EMessageFlags.Encrypted))
            {
                if (null == sharedKey || 0 == sharedKey.Length)
                    throw new ArgumentException(nameof(sharedKey));

                _payload = Encryptor.Decrypt(sharedKey, _payload);
            }

            if (_header.HasFlags(EMessageFlags.Compressed))
                _payload = Compressor.Decompress(_payload);

            return BinaryConverter.DeserializeObject(_payload, type) as IProtocolData;
        }
    }
}
