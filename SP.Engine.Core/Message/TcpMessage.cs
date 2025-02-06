using System;
using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using SP.Engine.Core.Protocol;
using SP.Engine.Core.Utility;

namespace SP.Engine.Core.Message
{
    public class TcpMessage : IMessage
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Header
        {
            public long SequenceNumber;
            public EProtocolId ProtocolId;
            public EOption Option;
            
            public static int Size => Marshal.SizeOf(typeof(Header));
        
            public bool HasOption(EOption option) => (Option & option) == option;
            public void EnableOption(EOption option) => Option |= option;
            public void DisableOption(EOption option) => Option &= ~option;
        }
        
        private static byte[] SerializeHeader(Header header)
        {
            var data = new byte[Header.Size];
            MemoryMarshal.Write(data, ref header);
            return data;
        }

        private static Header DeserializeHeader(ReadOnlySpan<byte> data)
        {
            if (data.Length < Header.Size)
                throw new ArgumentException("Byte array is too small for the target structure");
            
            return MemoryMarshal.Read<Header>(data);
        }
        
        public long SequenceNumber => _header.SequenceNumber;
        public EProtocolId ProtocolId => _header.ProtocolId;

        private Header _header;
        private byte[] _payload;
        
        public void SetSequenceNumber(long sequenceNumber)
        {
            _header.SequenceNumber = sequenceNumber;
        }
        
        public byte[] ToArray()
        {
            var headerBytes = SerializeHeader(_header);
            var payloadSize = _payload?.Length ?? 0;
            var sizeBytes = BitConverter.GetBytes(payloadSize);
            
            var result = new byte[headerBytes.Length + sizeBytes.Length + payloadSize];
            System.Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
            System.Buffer.BlockCopy( sizeBytes, 0, result, headerBytes.Length, sizeBytes.Length);
            if (null != _payload)
                System.Buffer.BlockCopy(_payload, 0, result, headerBytes.Length + sizeBytes.Length, payloadSize);
            
            return result;
        }

        public bool ReadBuffer(Buffer buffer)
        {
            var headerSize = Header.Size + sizeof(int);
            if (buffer.RemainSize < headerSize)
                return false;

            var span = buffer.Peek(headerSize);
            var payloadSize = BinaryPrimitives.ReadInt32LittleEndian(span[Header.Size..]);
            if (buffer.RemainSize < payloadSize + headerSize)
                return false;

            var headerSpan = buffer.Read(Header.Size);
            var length = buffer.Read<int>();
            var header = DeserializeHeader(headerSpan);
            
            _header = header;
            _payload = buffer.ReadBytes(length);
            return true;
        }
 
        public void SerializeProtocol(IProtocolData data, byte[] sharedKey)
        {
            _header.ProtocolId = data.ProtocolId;
            _payload = BinaryConverter.SerializeObject(data, data.GetType());
            if (null == _payload)
                throw new InvalidOperationException("Failed to serialize protocol");

            if (0 < data.CompressibleSize && data.CompressibleSize <= _payload.Length)
            {
                _header.EnableOption(EOption.Compress);
                _payload = Compressor.Compress(_payload);
            }

            if (!data.IsEncrypt) return;
            if (null == sharedKey || 0 == sharedKey.Length)
                throw new Exception("Invalid sharedKey");

            _header.EnableOption(EOption.Encrypt);
            _payload = Encryptor.Encrypt(sharedKey, _payload);
        }

        public IProtocolData DeserializeProtocol(Type type, byte[] sharedKey)
        {
            if (null == _payload || 0 == _payload.Length)
                return null;

            if (_header.HasOption(EOption.Encrypt))
            {
                if (null == sharedKey || 0 == sharedKey.Length)
                    throw new Exception("Invalid sharedKey");

                _payload = Encryptor.Decrypt(sharedKey, _payload);
            }

            if (_header.HasOption(EOption.Compress))
                _payload = Compressor.Decompress(_payload);

            return BinaryConverter.DeserializeObject(_payload, type) as IProtocolData;
        }
    }
}
