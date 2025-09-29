using System;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Networking
{
    public class UdpMessage : BaseMessage<UdpHeader>
    {
        public UdpMessage()
        {
            
        }
        
        public UdpMessage(UdpHeader header, ArraySegment<byte> payload)
            : base(header, payload)
        {
        }
        
        public void SetPeerId(PeerId peerId)
        {
            Header = new UdpHeaderBuilder()
                .From(Header)
                .WithPeerId(peerId)
                .Build();
        }

        public List<UdpFragment> ToSplit(int maxPayloadSize, uint fragmentId)
        {
            const int overhead = UdpHeader.HeaderSize + UdpFragmentHeader.HeaderSize;
            if (maxPayloadSize <= overhead)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadSize), $"mtu({maxPayloadSize}) <= overhead({overhead})");

            var maxSize = maxPayloadSize - overhead;
            var body = GetBodySpan();
            
            var totalCount = (byte)Math.Ceiling(body.Length / (float)maxSize);
            var header = new UdpHeaderBuilder()
                .From(Header)
                .AddFlag(HeaderFlags.Fragment)
                .Build();
      
            var fragments = new List<UdpFragment>();
            for (byte i = 0; i < totalCount; i++)
            {
                var offset = i * maxSize;
                var size = (ushort)Math.Min(maxSize, body.Length - offset);
                fragments.Add(new UdpFragment(
                    header,
                    new UdpFragmentHeader(fragmentId, i, totalCount, size),
                    new ArraySegment<byte>(body.ToArray(), offset, size))
                );
            }
            return fragments;
        }
        
        protected override UdpHeader CreateHeader(ushort id, HeaderFlags flags, int payloadLength)
        {
            return new UdpHeaderBuilder()
                .From(Header)
                .WithId(id)
                .WithPayloadLength(payloadLength)
                .AddFlag(flags)
                .Build();
        }
    }
}

