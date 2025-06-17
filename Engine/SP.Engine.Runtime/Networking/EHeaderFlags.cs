using System;

namespace SP.Engine.Runtime.Networking
{
    [Flags]
    public enum EHeaderFlags : byte
    {
        None = 0,
        Encrypted = 1 << 0, // 암호화됨
        Compressed = 1 << 1,// 압축됨
        Fragmentation = 1 << 2, // 조각화됨
        All = Encrypted | Compressed,
    }
}
