using System;
using System.Security.Cryptography;
using System.Threading;

namespace SP.Engine.Runtime.Security
{
    public class AesGcmEncryptor : IEncryptor, IDisposable
    {
        private const int NonceSize = SaltSize + CounterSize;
        private const int TagSize = 16;
        private const int SaltSize = 4;
        private const int CounterSize = 8;

        private AesGcm _aesGcm;
        private bool _disposed;
        
        private readonly byte[] _salt = new byte[SaltSize];
        private long _counter;

        public AesGcmEncryptor(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 32) throw new ArgumentException("AES-256 requires a 32-byte key.");
            _aesGcm = new AesGcm(key);
            RandomNumberGenerator.Fill(_salt);
        }

        public int GetCiphertextLength(int plainLength)
            => NonceSize + plainLength + TagSize;

        public int GetPlaintextLength(int cipherLength)
            => Math.Max(0, cipherLength - NonceSize - TagSize);

        public int Encrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            ThrowIfDisposed();
        
            // nonce 구성: salt(4) + counter(8)
            var nonce = destination[..NonceSize];
            var counter = Interlocked.Increment(ref _counter);
                
            _salt.CopyTo(nonce[..SaltSize]);
            nonce.WriteInt64(SaltSize, counter);
            
            // 암호화 수행
            _aesGcm.Encrypt(
                nonce, 
                source, 
                destination.Slice(NonceSize, source.Length), 
                destination.Slice(NonceSize + source.Length, TagSize));  
                
            return NonceSize + source.Length + TagSize;
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
                _aesGcm.Decrypt(nonce, ciphertext, tag, destination[..ciphertextLength]);
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
