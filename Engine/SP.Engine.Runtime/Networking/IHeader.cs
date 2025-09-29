using System;

namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        long SequenceNumber { get; }
        ushort Id { get; }
        HeaderFlags Flags { get; }
        int Length { get; }
        void WriteTo(Span<byte> buffer);
    }
}
