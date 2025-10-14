using System;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    public class AesCbcEncryptor : IEncryptor
    {
        private readonly byte[] _key;
        private const int IvSize = 16;
        private const int AesKeySize = 32;
        private const int BlockSize = 16;
        private bool _disposed;

        public AesCbcEncryptor(byte[] key)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (key.Length != AesKeySize) throw new ArgumentException("Session key must be 32 bytes", nameof(key));
            _key = (byte[])key.Clone();
        }
        
        public byte[] Encrypt(byte[] plain)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesCbcEncryptor));
            if (plain is null) throw new ArgumentNullException(nameof(plain));
            if (plain.Length == 0) plain = Array.Empty<byte>();
            
            var iv = new byte[IvSize];
            byte[] cipher = null;
            
            try
            {
                RandomNumberGenerator.Fill(iv);
                
                using var aes = Aes.Create();   
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

                var result = new byte[IvSize + cipher.Length];
                Buffer.BlockCopy(iv, 0, result, 0, IvSize);
                Buffer.BlockCopy(cipher, 0, result, IvSize, cipher.Length);
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(iv);
                CryptographicOperations.ZeroMemory(cipher);
            }
        }

        public byte[] Decrypt(byte[] cipher)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesCbcEncryptor));
            if (cipher is null) throw new ArgumentNullException(nameof(cipher));
            if (cipher.Length < IvSize + BlockSize)
                throw new ArgumentException("Encrypted data too short", nameof(cipher));
            
            var iv = new byte[IvSize];
            Buffer.BlockCopy(cipher, 0, iv, 0, iv.Length);
            var ctLen = cipher.Length - IvSize;
            if (ctLen % BlockSize != 0) 
                throw new ArgumentException("Ciphertext length invalid", nameof(cipher));

            var ct = new byte[ctLen];
            Buffer.BlockCopy(cipher, IvSize, ct, 0, ctLen);

            try
            {
                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                return decryptor.TransformFinalBlock(ct, 0, ctLen);
            }
            catch (CryptographicException ex)
            {
                throw new CryptographicException("Decryption failed.", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(iv);
                CryptographicOperations.ZeroMemory(ct);
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
