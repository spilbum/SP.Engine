using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SP.Engine.Core.Security
{
    internal static class Encryptor
    {
        private const int IvSize = 16;
        private const int AesKeySize = 32;

        public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
        {
            if (key.Length != AesKeySize)
                throw new ArgumentException("AES key must be 32 bytes");

            Span<byte> iv = stackalloc byte[IvSize];
            RandomNumberGenerator.Fill(iv);
            var ivBytes = iv.ToArray();

            using var aes = Aes.Create();
            aes.Key = key.ToArray();
            aes.IV = ivBytes;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var ciphertext = encryptor.TransformFinalBlock(plaintext.ToArray(), 0, plaintext.Length);

            var result = new byte[IvSize + ciphertext.Length];
            Buffer.BlockCopy(ivBytes, 0, result, 0, IvSize);
            Buffer.BlockCopy(ciphertext, 0, result, IvSize, ciphertext.Length);

            return result;
        }

        public static byte[] Decrypt(ReadOnlySpan<byte> encryptedData, ReadOnlySpan<byte> key)
        {
            if (key.Length != AesKeySize)
                throw new ArgumentException("AES key must be 32 bytes");

            if (encryptedData.Length < IvSize)
                throw new ArgumentException("Encrypted data is too short.");

            var iv = encryptedData[..IvSize];
            var ciphertext = encryptedData[IvSize..];

            using var aes = Aes.Create();
            aes.Key = key.ToArray();
            aes.IV = iv.ToArray();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext.ToArray(), 0, ciphertext.Length);
        }
    }
}
