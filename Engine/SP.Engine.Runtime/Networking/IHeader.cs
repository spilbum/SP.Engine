using System;

namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        HeaderFlags Flags { get; }
        ushort Id { get; }
        int Size { get; }
        void WriteTo(Span<byte> s);
    }
}
