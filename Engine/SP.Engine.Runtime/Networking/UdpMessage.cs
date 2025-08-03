using System;
using System.Collections.Generic;
using SP.Engine.Runtime.Protocol;

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

        public void SetSequenceNumber(long sequenceNumber)
        {
            Header = new UdpHeaderBuilder()
                .From(Header)
                .WithSequenceNumber(sequenceNumber)
                .Build();
        }
        
        public void SetPeerId(EPeerId peerId)
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
            var body = GetBody();
            
            var totalCount = (byte)Math.Ceiling(body.Length / (float)maxSize);
            var header = new UdpHeaderBuilder()
                .From(Header)
                .AddFlag(EHeaderFlags.Fragmentation)
                .Build();
      
            var fragments = new List<UdpFragment>();
            for (byte i = 0; i < totalCount; i++)
            {
                var offset = i * maxSize;
                var size = (ushort)Math.Min(maxSize, body.Length - offset);
                fragments.Add(new UdpFragment(
                    header,
                    new UdpFragmentHeader(fragmentId, i, totalCount, size),
                    new ArraySegment<byte>(body, offset, size)));
            }
            return fragments;
        }
        
        protected override UdpHeader CreateHeader(EProtocolId protocolId, EHeaderFlags flags, int payloadLength)
        {
            return new UdpHeaderBuilder()
                .From(Header)
                .WithProtocolId(protocolId)
                .WithPayloadLength(payloadLength)
                .AddFlag(flags)
                .Build();
        }
    }
}

