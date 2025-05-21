using System;

namespace SP.Engine.Runtime.Message
{
    [Flags]
    public enum EMessageFlags : byte
    {
        None = 0,
        Encrypted = 1 << 0, // 암호화됨
        Compressed = 1 << 1,// 압축됨
        Hmac = 1 << 2,      // hmac 포함
        All = Encrypted | Compressed | Hmac,
    }
}
