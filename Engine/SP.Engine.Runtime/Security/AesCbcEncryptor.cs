using System;
using System.Buffers;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    public class AesCbcEncryptor : IEncryptor
    {
        private const int IvSize = 16;
        private const int BlockSize = 16;
        private readonly byte[] _key;

        public AesCbcEncryptor(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            _key = (byte[])key.Clone();
        }

        public int GetCiphertextLength(int plainLength)
        {
            // PKCS7 Padding: 블록 크기 배수로 올림
            var padded = (plainLength / BlockSize + 1) * BlockSize;
            return IvSize + padded;
        }

        public int GetPlaintextLength(int cipherLength)
        {
            return cipherLength - IvSize; // 패딩 제거 전 최대 길이
        }

        public int Encrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            // IV 생성
            var iv = destination[..IvSize];
            RandomNumberGenerator.Fill(iv); 

            // IV를 AES에 설정
            var ivArray = iv.ToArray();
            aes.IV = ivArray;

            // 입력 데이터 준비 (패딩 추가)
            var plainLen = source.Length;
            var padLen = BlockSize - plainLen % BlockSize;
            var totalLen = plainLen + padLen;

            // 입력을 블록 크기에 맞추기 위해 임시 버퍼 대여
            var rentBuffer = ArrayPool<byte>.Shared.Rent(totalLen);
            try
            {
                // 원본 복사
                source.CopyTo(rentBuffer);

                // PKCS7 패딩 수동 적용
                for (var i = 0; i < padLen; i++)
                {
                    rentBuffer[plainLen + i] = (byte)padLen;
                }

                // 암호화
                using var encryptor = aes.CreateEncryptor();
                var encryptedBytes = encryptor.TransformFinalBlock(rentBuffer, 0, totalLen);
                new ReadOnlySpan<byte>(encryptedBytes).CopyTo(destination[IvSize..]);
                return IvSize + encryptedBytes.Length;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentBuffer);
            }
        }

        public int Decrypt(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var ivSpan = source[..IvSize];
            var cipherSpan = source[IvSize..];

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = ivSpan.ToArray(); 
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            using var decryptor = aes.CreateDecryptor();

            // 입력(Cipher)를 배열로 변환 (Rent)
            var rentInput = ArrayPool<byte>.Shared.Rent(cipherSpan.Length);
            try
            {
                cipherSpan.CopyTo(rentInput);
                
                var decryptedBytes = decryptor.TransformFinalBlock(rentInput, 0, cipherSpan.Length);
                if (decryptedBytes.Length == 0) throw new CryptographicException("Empty decrypted data");
                
                var lastByte = decryptedBytes[^1];
                if (lastByte > BlockSize || lastByte == 0)
                    throw new CryptographicException($"Invalid Padding value: {lastByte}");

                for (var i = 0; i < lastByte; i++)
                {
                    if (decryptedBytes[decryptedBytes.Length - 1 - i] != lastByte)
                        throw new CryptographicException("Broken Padding bytes");
                }

                var realLen = decryptedBytes.Length - lastByte;
                new ReadOnlySpan<byte>(decryptedBytes, 0, realLen).CopyTo(destination);
                return realLen;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentInput);
            }
        }
    }
}
