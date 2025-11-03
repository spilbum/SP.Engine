using System;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    public class AesCbcEncryptor : IEncryptor
    {
        private const int IvSize = 16;
        private const int AesKeySize = 32;
        private const int BlockSize = 16;
        private readonly byte[] _key;
        private bool _disposed;

        public AesCbcEncryptor(byte[] key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key.Length != AesKeySize) throw new ArgumentException("Session key must be 32 bytes", nameof(key));
            _key = (byte[])key.Clone();
        }

        public byte[] Encrypt(byte[] plain)
        {
            return Encrypt((ReadOnlySpan<byte>)plain);
        }

        public byte[] Decrypt(byte[] cipher)
        {
            return Decrypt((ReadOnlySpan<byte>)cipher);
        }

        public byte[] Encrypt(ReadOnlySpan<byte> plain)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesCbcEncryptor));

            var iv = new byte[IvSize];
            RandomNumberGenerator.Fill(iv);

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var enc = aes.CreateEncryptor();

                var p = plain.Length == 0 ? Array.Empty<byte>() : plain.ToArray();
                var ct = enc.TransformFinalBlock(p, 0, p.Length);

                var result = new byte[IvSize + ct.Length];
                Buffer.BlockCopy(iv, 0, result, 0, IvSize);
                Buffer.BlockCopy(ct, 0, result, IvSize, ct.Length);

                CryptographicOperations.ZeroMemory(ct);
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(iv);
            }
        }

        public byte[] Decrypt(ReadOnlySpan<byte> cipher)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesCbcEncryptor));
            if (cipher.Length < IvSize + BlockSize)
                throw new ArgumentException("Cipher too short (IV + >= 1 block required).", nameof(cipher));

            var c = cipher.ToArray();

            // IV 분리
            var iv = new byte[IvSize];
            Buffer.BlockCopy(c, 0, iv, 0, iv.Length);

            var ctLen = c.Length - IvSize;
            if (ctLen % BlockSize != 0)
                throw new ArgumentException("Ciphertext length is not a multiple of block size.", nameof(cipher));

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var dec = aes.CreateDecryptor();
                return dec.TransformFinalBlock(c, IvSize, ctLen);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Decryption failed.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(iv);
                CryptographicOperations.ZeroMemory(c);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CryptographicOperations.ZeroMemory(_key);
            GC.SuppressFinalize(this);
        }
    }
}
