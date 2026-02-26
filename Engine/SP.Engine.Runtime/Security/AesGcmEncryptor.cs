using System;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    public class AesGcmEncryptor : IEncryptor, IDisposable
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;

        private AesGcm _aesGcm;
        private bool _disposed;

        public AesGcmEncryptor(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 32) throw new ArgumentException("AES-256 requires a 32-byte key.");
            _aesGcm = new AesGcm(key);
        }

        public int GetCiphertextLength(int plainLength)
            => NonceSize + plainLength + TagSize;

        public int GetPlaintextLength(int cipherLength)
            => Math.Max(0, cipherLength - NonceSize - TagSize);

        public int Encrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            ThrowIfDisposed();

            // 1. Nonce 생성 
            var nonce = destination[..NonceSize];
            RandomNumberGenerator.Fill(nonce);

            // 2. 영역 분할: [Nonce(12)] [Ciphertext(N)] [Tag(16)]
            var ciphertextLength = source.Length;
            var ciphertext = destination.Slice(NonceSize, ciphertextLength);
            var tag = destination.Slice(NonceSize + ciphertextLength, TagSize);

            // 3. 암호화 수행
            _aesGcm.Encrypt(nonce, source, ciphertext, tag);

            return NonceSize + ciphertextLength + TagSize;
        }

        public int Decrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            ThrowIfDisposed();

            if (source.Length < NonceSize + TagSize)
                throw new CryptographicException("Ciphertext too short.");

            // 1. 영역 분해
            var nonce = source[..NonceSize];
            var ciphertextLength = source.Length - NonceSize - TagSize;
            var ciphertext = source.Slice(NonceSize, ciphertextLength);
            var tag = source.Slice(NonceSize + ciphertextLength, TagSize);

            // 2. 복호화 및 인증
            try
            {
                _aesGcm.Decrypt(nonce, ciphertext, tag, destination.Slice(0, ciphertextLength));
                return ciphertextLength;
            }
            catch (CryptographicException e)
            {
                // 데이터가 변조되었거나 키가 틀린 경우
                throw new CryptographicException("Decryption failed (Integrity check failed).", e);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _aesGcm?.Dispose();
            _aesGcm = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesGcmEncryptor));
        }
    }
}
