using System;

namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        int HeaderLength { get; }
        int PayloadLength { get; }
        ushort ProtocolId { get; }
        bool HasFlag(HeaderFlags flags);
        void WriteTo(Span<byte> destination);
    }
}
