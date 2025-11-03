using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SP.Engine.Runtime.Security
{
    public class DiffieHellman : IDisposable
    {
        private readonly DhParameters _parameters;
        private readonly byte[] _publicKeyBytes;
        private bool _disposed;
        private DhPrivateKey _privateKey;

        public DiffieHellman(DhKeySize keySize)
        {
            KeySize = keySize;
            _parameters = DhParameters.From(keySize);

            var keyPair = DhKeyPair.Generate(_parameters);
            _privateKey = keyPair.PrivateKey;
            var pub = keyPair.PublicKey;
            _publicKeyBytes = DhUtil.PadToLength(
                pub.KeyValue.ToByteArray(true, true),
                _parameters.PublicKeyByteLength);
        }

        public DhKeySize KeySize { get; }

        public byte[] PublicKey
        {
            get
            {
                var dst = new byte[_publicKeyBytes.Length];
                Buffer.BlockCopy(_publicKeyBytes, 0, dst, 0, dst.Length);
                return dst;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_privateKey != null)
            {
                var skBytes = _privateKey.KeyValue.ToByteArray(true, true);
                CryptographicOperations.ZeroMemory(skBytes);
                _privateKey = null;
            }

            CryptographicOperations.ZeroMemory(_publicKeyBytes);
            GC.SuppressFinalize(this);
        }

        public byte[] DeriveSharedKey(byte[] otherPublicKey)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DiffieHellman));
            if (otherPublicKey is null) throw new ArgumentNullException(nameof(otherPublicKey));
            if (otherPublicKey.Length != _parameters.PublicKeyByteLength)
                throw new ArgumentException("Invalid public key length", nameof(otherPublicKey));

            // 상대 공개키 파싱/검증
            var y = new BigInteger(otherPublicKey, isBigEndian: true, isUnsigned: true);

            if (y <= BigInteger.One || y >= _parameters.P - BigInteger.One)
                throw new ArgumentException("Invalid public key range", nameof(otherPublicKey));

            if (_parameters.Q > BigInteger.One)
                if (BigInteger.ModPow(y, _parameters.Q, _parameters.P) != BigInteger.One)
                    throw new ArgumentException("Invalid other public key (failed subgroup check).",
                        nameof(otherPublicKey));

            // 공유 비밀 z 계산
            var zBig = BigInteger.ModPow(y, _privateKey.KeyValue, _parameters.P);
            var z = zBig.ToByteArray(true, true);

            // z == 0/1 방지
            if (z.Length == 0 || (z.Length == 1 && z[0] == 1))
            {
                Array.Clear(z, 0, z.Length);
                throw new CryptographicException("Week shared secret.");
            }

            // 고정 길이 정규화
            var zFixed = DhUtil.PadToLength(z, _parameters.PublicKeyByteLength);
            var otherFixed = DhUtil.PadToLength(otherPublicKey, _parameters.PublicKeyByteLength);

            var (a, b) = ByteArrayCompare(_publicKeyBytes, otherFixed) <= 0
                ? (_publicKeyBytes, otherFixed)
                : (otherFixed, _publicKeyBytes);

            var salt = DhUtil.Sha256Concat(a, b);
            var info = Encoding.UTF8.GetBytes($"SP.Engine.DH|v1|{(int)KeySize}");

            var prk = HkdfUtil.Extract(salt, zFixed);
            var okm = HkdfUtil.Expand(prk, info, 32);

            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(z);
            CryptographicOperations.ZeroMemory(zFixed);

            return okm;

            static int ByteArrayCompare(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
            {
                var n = Math.Min(x.Length, y.Length);
                for (var i = 0; i < n; i++)
                    if (x[i] != y[i])
                        return x[i] - y[i];
                return x.Length - y.Length;
            }
        }
    }
}
