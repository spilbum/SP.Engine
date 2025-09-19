using System;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    public interface IEncryptor : IDisposable
    {
        byte[] Encrypt(ReadOnlySpan<byte> plain);
        byte[] Decrypt(ReadOnlySpan<byte> cipher);
    }
    public sealed class Encryptor : IEncryptor
    {
        private readonly byte[] _key;
        private const int IvSize = 16;
        private const int AesKeySize = 32;
        private const int BlockSize = 16;

        public Encryptor(byte[] key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key.Length != AesKeySize) throw new ArgumentException("Session key must be 32 bytes", nameof(key));
            _key = key;
        }
        
        public byte[] Encrypt(ReadOnlySpan<byte> plain)
        {
            var iv = new byte[IvSize];
            RandomNumberGenerator.Fill(iv);

            try
            {
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                var ciphertext = encryptor.TransformFinalBlock(plain.ToArray(), 0, plain.Length);

                var result = new byte[IvSize + ciphertext.Length];
                Buffer.BlockCopy(iv, 0, result, 0, IvSize);
                Buffer.BlockCopy(ciphertext, 0, result, IvSize, ciphertext.Length);
                
                Array.Clear(ciphertext, 0, ciphertext.Length);
                return result;
            }
            finally
            {
                Array.Clear(iv, 0, iv.Length);
            }
        }

        public byte[] Decrypt(ReadOnlySpan<byte> input)
        {
            if (input.Length < IvSize + 1) throw new ArgumentException("Encrypted data too short", nameof(input));
            var chipherLen = input.Length - IvSize;
            if (chipherLen % BlockSize != 0) throw new ArgumentException("Chiphertext length invalid", nameof(input));

            var iv = input.Slice(0, IvSize).ToArray();
            var ciphertext = input.Slice(IvSize).ToArray();

            try
            {
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                try
                {
                    var plain = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                    return plain;
                }
                catch (CryptographicException)
                {
                    throw new CryptographicException("Decryption failed");
                }
            }
            finally
            {
                Array.Clear(iv, 0, iv.Length);
                Array.Clear(ciphertext, 0, ciphertext.Length);
            }
        }

        public void Dispose()
        {
            if (_key != null)
                Array.Clear(_key, 0, _key.Length);
        }
    }
}
