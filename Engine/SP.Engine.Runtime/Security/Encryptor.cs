using System;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    internal static class Encryptor
    {
        private const int IvSize = 16;
        private const int AesKeySize = 32;

        public static byte[] Encrypt(byte[] input, byte[] aesKey)
        {
            if (aesKey.Length != AesKeySize)
                throw new ArgumentException("AES key must be 32 bytes", nameof(aesKey));

            // 랜덤으로 IV 생성
            var iv = new byte[IvSize];
            RandomNumberGenerator.Fill(iv);

            using var aes = Aes.Create();
            aes.Key = aesKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            var ciphertext = encryptor.TransformFinalBlock(input, 0, input.Length);

            var result = new byte[IvSize + ciphertext.Length];
            Buffer.BlockCopy(iv, 0, result, 0, IvSize);
            Buffer.BlockCopy(ciphertext, 0, result, IvSize, ciphertext.Length);
            return result;
        }

        public static byte[] Decrypt(byte[] input, byte[] aesKey)
        {
            if (aesKey.Length != AesKeySize)
                throw new ArgumentException("AES key must be 32 bytes");

            if (input.Length < IvSize)
                throw new ArgumentException("Encrypted data is too short.");

            var iv = input[..IvSize];
            var ciphertext = input[IvSize..];

            using var aes = Aes.Create();
            aes.Key = aesKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }
    }
}
