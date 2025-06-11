using System;

namespace SP.Engine.Runtime.Message
{
    public readonly struct UdpFragment
    {
        public readonly struct Header
        {
            private const int IndexOffset = 0;
            private const int TotalCountOffset = IndexOffset + sizeof(byte);
            private const int LengthOffset = TotalCountOffset + sizeof(byte);
            public const int HeaderSize = LengthOffset + sizeof(ushort);
        
            public byte Index { get; }
            public byte TotalCount { get; }
            public ushort Length { get; }
        
            public Header(byte index, byte totalCount, ushort length)
            {
                Index = index;
                TotalCount = totalCount;
                Length = length;
            }

            public void WriteTo(Span<byte> span)
            {
                span[IndexOffset] = Index;
                span[TotalCountOffset] = TotalCount;
                span.WriteUInt16(LengthOffset, Length);
            }

            public static bool TryParse(ReadOnlySpan<byte> span, out Header header)
            {
                header = default;
                if (span.Length < HeaderSize)
                    return false;
            
                header = new Header(
                    span[IndexOffset],
                    span[TotalCountOffset],
                    span.ReadUInt16(LengthOffset));
                return true;
            }
        }

        private readonly UdpHeader _udpHeader;
        private readonly Header _header;
        private readonly ArraySegment<byte> _payload;
        
        public byte Index => _header.Index;
        public byte TotalCount => _header.TotalCount;
        public ArraySegment<byte> Payload => _payload;
        public int Length => UdpHeader.HeaderSize + Header.HeaderSize + _payload.Count;

        public UdpFragment(UdpHeader udpHeader, Header header, ArraySegment<byte> payload)
        {
            _udpHeader = udpHeader;
            _header = header;
            _payload = payload;
        }

        public void WriteTo(Span<byte> buffer)
        {
            if (buffer.Length < Length)
                throw new ArgumentException("Destination buffer is too small", nameof(buffer));
            var offset = 0;
            _udpHeader.WriteTo(buffer);
            offset += UdpHeader.HeaderSize;
            _header.WriteTo(buffer[offset..]);
            offset += Header.HeaderSize;
            _payload.AsSpan().CopyTo(buffer[offset..]);
        }
        
        public static bool TryParse(ReadOnlySpan<byte> span, out UdpFragment fragment)
        {
            fragment = default;

            var offset = 0;
            if (!UdpHeader.TryParse(span.Slice(offset, TcpHeader.HeaderSize), out var udpHeader)) return false;
            offset += UdpHeader.HeaderSize;
            if (!Header.TryParse(span.Slice(offset, Header.HeaderSize), out var header)) return false;
            offset += Header.HeaderSize;
            
            if (span.Length < offset + header.Length) 
                return false;

            var payload = span.Slice(offset, header.Length).ToArray();
            fragment = new UdpFragment(udpHeader, header, new ArraySegment<byte>(payload));
            return true;
        }
    }
}
