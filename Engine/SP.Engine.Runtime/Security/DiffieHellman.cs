using System;
using System.Numerics;
using System.Security.Cryptography;
using SP.Common.Buffer;

namespace SP.Engine.Runtime.Security
{
    public class DiffieHellman : IDisposable
    {
        private DhPrivateKey _privateKey;
        private readonly DhPublicKey _publicKey;
        private readonly DhParameters _parameters;
        private bool _disposed;
        
        public EDhKeySize KeySize { get; }

        public DiffieHellman(EDhKeySize keySize)
        {
            KeySize = keySize;
            _parameters = DhParameters.From(keySize);
            
            var keyPair = DhKeyPair.Generate(_parameters);
            _privateKey = keyPair.PrivateKey;
            _publicKey = keyPair.PublicKey;
        }

        public byte[] PublicKey 
            => DhUtil.PadToLength(
                _publicKey.KeyValue.ToByteArray(isUnsigned: true, isBigEndian: true),
                _parameters.PublicKeyByteLength);

        public byte[] DeriveSharedKey(byte[] otherPublicKey)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DiffieHellman));
            if (otherPublicKey is null || otherPublicKey.Length == 0)
                throw new ArgumentNullException(nameof(otherPublicKey));
            
            // 상대 공개키 파싱/검증
            var y = new BigInteger(otherPublicKey, isBigEndian: true, isUnsigned: true);
            if (!DhUtil.IsValidPublicKey(y, _parameters.P))
                throw new ArgumentException("Invalid other public key. Key is out of bounds for DH parameter P.");

            // 공유 비밀 Z = Y^x mod p
            var z = BigInteger.ModPow(y, _privateKey.KeyValue, _parameters.P)
                                    .ToByteArray(isUnsigned: true, isBigEndian: true);

            // 고정 길이 정규화
            var zFixed = DhUtil.PadToLength(z, _parameters.PublicKeyByteLength);

            try
            {
                var selfPubFixed = PublicKey;
                var otherPubFixed = DhUtil.PadToLength(otherPublicKey, _parameters.PublicKeyByteLength);
                var (a, b) = ByteArrayCompare(selfPubFixed, otherPubFixed) <= 0
                    ? (selfPubFixed, otherPubFixed)
                    : (otherPubFixed, selfPubFixed);
                
                using var sha256 = SHA256.Create();

                var buf = new byte[a.Length + b.Length + zFixed.Length];
                Buffer.BlockCopy(a, 0, buf, 0, a.Length);
                Buffer.BlockCopy(b, 0, buf, a.Length, b.Length);
                Buffer.BlockCopy(zFixed, 0, buf, a.Length + b.Length, zFixed.Length);
                
                var sharedKey = sha256.ComputeHash(buf);
                Array.Clear(buf, 0, buf.Length);
                return sharedKey;
            }
            finally
            {
                Array.Clear(z, 0, z.Length);
                Array.Clear(zFixed, 0, zFixed.Length);
            }

            static int ByteArrayCompare(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
            {
                var n = Math.Min(x.Length, y.Length);
                for (var i = 0; i < n; i++)
                {
                    if (x[i] != y[i]) return x[i] - y[i];
                }
                return x.Length - y.Length;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_privateKey != null)
            {
                var skBytes = _privateKey.KeyValue.ToByteArray(isUnsigned: true, isBigEndian: true);
                Array.Clear(skBytes, 0, skBytes.Length);
                _privateKey = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
