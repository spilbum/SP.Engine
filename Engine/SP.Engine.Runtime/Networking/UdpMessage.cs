using System;
using System.Buffers;
using SP.Core;
using SP.Core.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : MessageBase<UdpHeader>
    {
        public UdpMessage()
        {
        }
        
        public UdpMessage(UdpHeader header, IMemoryOwner<byte> bufferOwner) : base(header, bufferOwner)
        {
        }

        protected override int HeaderLength => UdpHeader.ByteSize;

        public void SetSessionId(long sessionId)
        {
            Header = new UdpHeaderBuilder()
                .From(Header)
                .WithSessionId(sessionId)
                .Build();
            
            UpdateHeaderInBuffer();
        }

        public void WriteFragmentTo(
            Span<byte> destination, 
            uint fragId,
            byte index,
            byte totalCount, 
            int bodyOffset, 
            ushort fragLength)
        {
            const int fragHeaderLen = FragmentHeader.ByteSize;
            
            var udpHeader = new UdpHeaderBuilder()
                .From(Header)
                .WithFragmented(1)
                .WithPayloadLength(fragHeaderLen + fragLength)
                .Build();
            
            udpHeader.WriteTo(destination[..HeaderLength]);

            var fragHeader = new FragmentHeader(fragId, index, totalCount, fragLength);
            fragHeader.WriteTo(destination.Slice(HeaderLength, fragHeaderLen));

            var srcPayload = PayloadSpan.Slice(bodyOffset, fragLength);
            srcPayload.CopyTo(destination[(HeaderLength + fragHeaderLen)..]);
        }

        public bool TryExtractFragments(ushort maxFragmentSize, out (PooledBuffer Buffer, int Length)[] fragments)
        {
            fragments = null;
            
            const int headerSize = UdpHeader.ByteSize;
            const int fragHeaderSize = FragmentHeader.ByteSize;
            
            var maxPayloadPerFrag = maxFragmentSize - headerSize - fragHeaderSize;
            if (maxPayloadPerFrag <= 0) return false;

            var totalCount = (byte)Math.Ceiling((double)PayloadLength / maxPayloadPerFrag);
            if (totalCount == 0) return false;
            
            var result = new (PooledBuffer Buffer, int Length)[totalCount];
            var fragId = unchecked((uint)Guid.NewGuid().GetHashCode());

            try
            {
                for (byte index = 0; index < totalCount; index++)
                {
                    var offset = index * maxPayloadPerFrag;
                    var fragPayloadLength = (ushort)Math.Min(PayloadLength - offset, maxPayloadPerFrag);
                    var totalFragSize = headerSize + fragHeaderSize + fragPayloadLength;

                    var buffer = new PooledBuffer(totalFragSize);
                    var destination = buffer.Memory.Span;

                    var header = new UdpHeaderBuilder()
                        .From(Header)
                        .WithFragmented(1)
                        .WithPayloadLength(fragHeaderSize + fragPayloadLength)
                        .Build();
                    header.WriteTo(destination[..headerSize]);
                    
                    var fragHeader = new FragmentHeader(fragId, index, totalCount, fragPayloadLength);
                    fragHeader.WriteTo(destination.Slice(headerSize, fragHeaderSize));
                    
                    var sourcePayload = PayloadSpan.Slice(offset, fragPayloadLength);
                    sourcePayload.CopyTo(destination[(headerSize + fragHeaderSize)..]);
                    
                    result[index] = (buffer, totalFragSize);
                }
                
                fragments = result;
                return true;
            }
            catch
            {
                for (var i = 0; i < totalCount; ++i)
                {
                    if (result[i].Buffer == null) continue;
                    result[i].Buffer.Dispose();
                    result[i].Buffer = null;
                }
                return false;
            }
        }

        protected override UdpHeader CreateHeader(HeaderFlags flags, ushort protocolId, int payloadLength)
        {
            return new UdpHeaderBuilder()
                .From(Header)
                .AddFlag(flags)
                .WithProtocolId(protocolId)
                .WithPayloadLength(payloadLength)
                .Build();
        }
    }
}
