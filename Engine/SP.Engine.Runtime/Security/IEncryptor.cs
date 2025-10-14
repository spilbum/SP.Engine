using System;

namespace SP.Engine.Runtime.Security
{
    public interface IEncryptor : IDisposable
    {
        byte[] Encrypt(byte[] plain);
        byte[] Decrypt(byte[] cipher);
    }
}
