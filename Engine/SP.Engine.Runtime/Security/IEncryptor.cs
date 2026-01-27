using System;

namespace SP.Engine.Runtime.Security
{
    public interface IEncryptor
    {
        int GetCiphertextLength(int plainLength);
        int GetPlaintextLength(int cipherLength);

        int Encrypt(ReadOnlySpan<byte> source, Span<byte> destination);
        int Decrypt(ReadOnlySpan<byte> source, Span<byte> destination);
    }
}
