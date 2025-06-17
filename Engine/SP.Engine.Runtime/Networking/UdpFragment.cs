using System;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct UdpFragment
    {
        private readonly UdpHeader _udpHeader;
        private readonly UdpFragmentHeader _udpFragmentHeader;
        private readonly ArraySegment<byte> _payload;
        
        public uint Id => _udpFragmentHeader.Id;
        public byte Index => _udpFragmentHeader.Index;
        public byte TotalCount => _udpFragmentHeader.TotalCount;
        public UdpHeader UdpHeader => _udpHeader;
        public ArraySegment<byte> Payload => _payload;

        public UdpFragment(UdpHeader udpHeader, UdpFragmentHeader udpFragmentHeader, ArraySegment<byte> payload)
        {
            _udpHeader = udpHeader;
            _udpFragmentHeader = udpFragmentHeader;
            _payload = payload;
        }

        public ArraySegment<byte> Serialize()
        {
            var buffer = new byte[UdpHeader.HeaderSize + UdpFragmentHeader.HeaderSize + _payload.Count];
            _udpHeader.WriteTo(buffer.AsSpan(0, UdpHeader.HeaderSize));
            _udpFragmentHeader.WriteTo(buffer.AsSpan(UdpHeader.HeaderSize, UdpFragmentHeader.HeaderSize));
            _payload.CopyTo(buffer, UdpHeader.HeaderSize + UdpFragmentHeader.HeaderSize);
            return new ArraySegment<byte>(buffer);
        }

        public static bool TryParse(ReadOnlySpan<byte> span, out UdpFragment fragment)
        {
            fragment = default;

            var offset = 0;
            if (!UdpHeader.TryParse(span.Slice(offset, UdpHeader.HeaderSize), out var udpHeader)) return false;
            offset += UdpHeader.HeaderSize;
            if (!UdpFragmentHeader.TryParse(span.Slice(offset, UdpFragmentHeader.HeaderSize), out var header)) return false;
            offset += UdpFragmentHeader.HeaderSize;
            
            if (span.Length < offset + header.PayloadLength) 
                return false;

            var payloadSpan = span.Slice(offset, header.PayloadLength);
            fragment = new UdpFragment(udpHeader, header, new ArraySegment<byte>(payloadSpan.ToArray()));
            return true;
        }
    }
}
