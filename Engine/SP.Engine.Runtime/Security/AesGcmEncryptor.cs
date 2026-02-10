using System;
using System.Buffers;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace SP.Engine.Runtime.Security
{
    public class AesGcmEncryptor : IEncryptor, IDisposable
    {
        /// <summary>
        /// GCM 표준 Nonce 크기
        /// </summary>
        private const int NonceSize = 12;
        /// <summary>
        ///128-bit 인증 태그
        /// </summary>
        private const int TagSize = 16;

        private readonly KeyParameter _keyParam;
        private readonly SecureRandom _random;
        private bool _disposed;

        public AesGcmEncryptor(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 32) throw new ArgumentException("AES-256 requires a 32-byte key.");

            _keyParam = new KeyParameter((byte[])key.Clone());
            _random = new SecureRandom();
        }

        public int GetCiphertextLength(int plainLength)
            => NonceSize + plainLength + TagSize;

        public int GetPlaintextLength(int cipherLength)
            => Math.Max(0, cipherLength - NonceSize - TagSize);

        public int Encrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            ThrowIfDisposed();
            
            // Nonce 생성 및 배치
            var nonce = new byte[NonceSize];
            _random.NextBytes(nonce);
            nonce.CopyTo(destination[..NonceSize]);

            // GCM 엔진 초기화
            var cipher = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(_keyParam, TagSize * 8, nonce);
            cipher.Init(true, parameters);

            var outSize = cipher.GetOutputSize(source.Length);
            var inputBuffer = ArrayPool<byte>.Shared.Rent(source.Length);
            var outputBuffer = ArrayPool<byte>.Shared.Rent(outSize);
            
            try
            {
                source.CopyTo(inputBuffer);
                
                var len = cipher.ProcessBytes(inputBuffer, 0, source.Length, outputBuffer, 0);
                len += cipher.DoFinal(outputBuffer, len);
            
                // 결과 복사 (Ciphertext + Tag)
                outputBuffer.AsSpan(0, len).CopyTo(destination[NonceSize..]);
                return NonceSize + len;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(inputBuffer);
                CryptographicOperations.ZeroMemory(outputBuffer);
                ArrayPool<byte>.Shared.Return(inputBuffer);
                ArrayPool<byte>.Shared.Return(outputBuffer);
            }
        }

        public int Decrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            ThrowIfDisposed();
            
            if (source.Length < NonceSize + TagSize)
                throw new CryptographicException("Ciphertext too short.");
            
            var nonce = new byte[NonceSize];
            source[..NonceSize].CopyTo(nonce);
            
            var cipherDataLen = source.Length - NonceSize;
            
            var cipher = new GcmBlockCipher(new AesEngine());
            var parameters = new AeadParameters(_keyParam, TagSize * 8, nonce);
            cipher.Init(false, parameters);
            
            var inputBuffer = ArrayPool<byte>.Shared.Rent(cipherDataLen);
            var outputBuffer = ArrayPool<byte>.Shared.Rent(cipher.GetOutputSize(cipherDataLen));
            
            try
            {
                source[NonceSize..].CopyTo(inputBuffer);
                
                var len = cipher.ProcessBytes(inputBuffer, 0, cipherDataLen, outputBuffer, 0);
                len += cipher.DoFinal(outputBuffer, len);
                
                outputBuffer.AsSpan(0, len).CopyTo(destination);
                return len;
            }
            catch (Exception e)
            {
                throw new CryptographicException("Decryption failed (Integrity check failed).", e);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(inputBuffer);
                CryptographicOperations.ZeroMemory(outputBuffer);
                ArrayPool<byte>.Shared.Return(inputBuffer);
                ArrayPool<byte>.Shared.Return(outputBuffer);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CryptographicOperations.ZeroMemory(_keyParam.GetKey());
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesGcmEncryptor));
        }
    }
}
