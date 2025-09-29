using System;

namespace SP.Engine.Runtime.Networking
{
    [Flags]
    public enum HeaderFlags : byte
    {
        None = 0,
        Encrypt = 1 << 0, // 암호화
        Compress = 1 << 1,// 압축
        Fragment = 1 << 2, // 조각화
    }
}
