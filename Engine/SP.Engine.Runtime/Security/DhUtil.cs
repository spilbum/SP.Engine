using System;
using System.Numerics;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    internal static class DhUtil
    {
        private static readonly BigInteger MinPublicKey = new BigInteger(2);
        private static BigInteger MaxPublicKey(BigInteger p) => p - new BigInteger(2);

        public static bool IsValidPublicKey(BigInteger key, BigInteger p)
            => key >= MinPublicKey && key <= MaxPublicKey(p);

        public static byte[] PadToLength(byte[] input, int length)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            
            if (input.Length == length) return input;
            if (input.Length > length)
                throw new ArgumentException(
                    $"Input length ({input.Length}) exceeds target length ({length}).",
                    nameof(length));

            var padded = new byte[length];
            Buffer.BlockCopy(input, 0, padded, length - input.Length, input.Length);
            return padded;
        }
    }
}
