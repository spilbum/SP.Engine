using System;
using System.Collections.Generic;
using SP.Core.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : MessageBase<UdpHeader, UdpMessage>
    {
        protected override int HeaderLength => UdpHeader.ByteSize;

        public void SetSessionId(long sessionId)
        {
            _header = new UdpHeader(
                _header.Flags,
                sessionId,
                _header.ProtocolId,
                _header.Fragmented,
                _header.PayloadLength
            );
            
            UpdateHeaderInBuffer();
        }

        public bool TryGetBufferOwner(out BufferOwner bufferOwner, out int length)
        {
            bufferOwner = null;
            length = 0;
            if (!TryGetBuffer(out var memory)) return false;

            var buffer = BufferOwnerPool.Rent(memory.Length);
            memory.CopyTo(buffer.Memory);
            
            bufferOwner = buffer;
            length = memory.Length;
            return true;
        }

        public bool TryGetFragments(int maxFragmentSize, out List<(BufferOwner Buffer, int Length)> fragments)
        {
            fragments = null;
            if (!TryGetBuffer(out var memory)) return false;
            
            const int headerSize = UdpHeader.ByteSize;
            const int fragHeaderSize = FragmentHeader.ByteSize;
            var maxPayloadPerFrag = maxFragmentSize - headerSize - fragHeaderSize;
            if (maxPayloadPerFrag <= 0) return false;

            var payloadSpan = memory.Span.Slice(headerSize, PayloadLength);
            var totalFragCount = (byte)Math.Ceiling((double)payloadSpan.Length / maxPayloadPerFrag);
            var fragId = unchecked((uint)Guid.NewGuid().GetHashCode());
            
            fragments = new List<(BufferOwner Buffer, int Length)>();
            
            for (byte index = 0; index < totalFragCount; index++)
            {
                var offset = index * maxPayloadPerFrag;
                var fragPayloadLength = (ushort)Math.Min(payloadSpan.Length - offset, maxPayloadPerFrag);
                var totalLength = headerSize + fragHeaderSize + fragPayloadLength;

                var buffer = BufferOwnerPool.Rent(totalLength);

                try
                {
                    var header = new UdpHeader(
                        _header.Flags,
                        _header.SessionId,
                        _header.ProtocolId,
                        1,
                        fragHeaderSize + fragPayloadLength
                    );
                    header.WriteTo(buffer[..headerSize]);

                    var fragHeader = new FragmentHeader(fragId, index, totalFragCount, fragPayloadLength);
                    fragHeader.WriteTo(buffer.Slice(headerSize, fragHeaderSize));
                
                    var span = payloadSpan.Slice(offset, fragPayloadLength);
                    span.CopyTo(buffer.Slice(headerSize + fragHeaderSize, fragPayloadLength));

                    fragments.Add((buffer, totalLength));
                }
                catch
                {
                    buffer.Dispose();
                    foreach (var item in fragments) item.Buffer.Dispose();
                    return false;
                }
            } 
            
            return true;
        }

        protected override UdpHeader CreateHeader(HeaderFlags flags, ushort protocolId, int payloadLength)
        {
            return new UdpHeader(
                _header.Flags | flags,
                _header.SessionId,
                protocolId,
                _header.Fragmented,
                payloadLength
            );
        }
    }
}
