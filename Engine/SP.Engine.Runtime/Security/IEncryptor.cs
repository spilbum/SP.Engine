using System;

namespace SP.Engine.Runtime.Security
{
    public interface IEncryptor : IDisposable
    {
        byte[] Encrypt(ReadOnlySpan<byte> plain);
        byte[] Decrypt(ReadOnlySpan<byte> cipher);

        byte[] Encrypt(byte[] plain);
        byte[] Decrypt(byte[] cipher);
    }
}
