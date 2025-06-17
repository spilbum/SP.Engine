using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        long SequenceNumber { get; }
        EProtocolId ProtocolId { get; }
        EHeaderFlags Flags { get; }
        int Length { get; }
        void WriteTo(Span<byte> buffer);
    }
}
