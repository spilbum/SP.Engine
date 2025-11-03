using System;
using System.Numerics;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    internal static class DhUtil
    {
        public static byte[] Sha256Concat(byte[] a, byte[] b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));

            using var sha256 = SHA256.Create();
            if (a.Length > 0) sha256.TransformBlock(a, 0, a.Length, null, 0);
            sha256.TransformFinalBlock(b, 0, b.Length);
            return sha256.Hash;
        }

        public static byte[] PadToLength(byte[] input, int length)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Length == length) return (byte[])input.Clone();
            if (input.Length > length)
            {
                // unsigned big-endian에서 선행 0이 있을 수 있다(가끔 BigInteger 변환에서 생김)
                var trimmed = new byte[length];
                Buffer.BlockCopy(input, input.Length - length, trimmed, 0, length);
                return trimmed;
            }

            var res = new byte[length];
            Buffer.BlockCopy(input, 0, res, length - input.Length, input.Length);
            return res;
        }

        // 랜덤 BigInteger < max (exclusive)
        public static BigInteger RandomBigIntegerLessThan(BigInteger maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            var bytes = (maxExclusive.GetBitLength() + 7) / 8;
            var tmp = new byte[bytes + 1]; // extra to ensure sign bit handled
            try
            {
                while (true)
                {
                    RandomNumberGenerator.Fill(tmp);
                    tmp[^1] = 0; // positive
                    var v = new BigInteger(tmp, true, true);
                    if (v < maxExclusive) return v;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(tmp);
            }
        }

        // extension helper: BigInteger 비트 길이
        public static int GetBitLength(this BigInteger value)
        {
            var bytes = value.ToByteArray(true, true);
            if (bytes.Length == 0) return 0;
            var msb = bytes[0];
            var bits = (bytes.Length - 1) * 8;
            // msb top bit position
            var top = 8;
            while (top > 0 && (msb & (1 << (top - 1))) == 0) top--;
            return bits + top;
        }
    }

    // HKDF util (RFC 5869) using HMAC-SHA256
    public static class HkdfUtil
    {
        public static byte[] Extract(byte[] salt, byte[] ikm)
        {
            using var hmac = new HMACSHA256(salt ?? Array.Empty<byte>());
            return hmac.ComputeHash(ikm ?? Array.Empty<byte>());
        }

        public static byte[] Expand(byte[] prk, byte[] info, int length)
        {
            if (prk == null) throw new ArgumentNullException(nameof(prk));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));

            const int hashLen = 32;
            var n = (length + hashLen - 1) / hashLen;
            if (n > 255) throw new ArgumentException("Cannot expand to more than 255 blocks");

            var okm = new byte[length];
            var previous = Array.Empty<byte>();
            using var hmac = new HMACSHA256(prk);
            var written = 0;
            for (var i = 1; i <= n; i++)
            {
                hmac.Initialize();

                hmac.TransformBlock(previous, 0, previous.Length, null, 0);
                if (info != null && info.Length > 0)
                    hmac.TransformBlock(info, 0, info.Length, null, 0);
                hmac.TransformFinalBlock(new[] { (byte)i }, 0, 1);
                var t = hmac.Hash;
                var copyLen = Math.Min(hashLen, length - written);
                Buffer.BlockCopy(t, 0, okm, written, copyLen);
                written += copyLen;

                previous = t;
            }

            CryptographicOperations.ZeroMemory(previous);
            return okm;
        }
    }
}
