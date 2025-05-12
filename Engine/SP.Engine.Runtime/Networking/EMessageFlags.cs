using System;

namespace SP.Engine.Runtime.Networking
{
    [Flags]
    public enum EMessageFlags : byte
    {
        None = 0,
        Encrypted = 1 << 0,  // 암호화됨
        Compressed = 1 << 1, // 압축됨
    }
}
