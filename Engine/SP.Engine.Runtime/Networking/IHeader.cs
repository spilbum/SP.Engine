using System;

namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        HeaderFlags Flags { get; }
        ushort MsdId { get; }
        int Size { get; }
    }
}
