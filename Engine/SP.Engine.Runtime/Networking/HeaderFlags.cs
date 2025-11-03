using System;

namespace SP.Engine.Runtime.Networking
{
    [Flags]
    public enum HeaderFlags : byte
    {
        None = 0,
        Encrypted = 1 << 0, // 암호화
        Compressed = 1 << 1 // 압축
    }
}
