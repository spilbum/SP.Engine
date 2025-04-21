using System.Linq;

namespace SP.Engine.Core.Utility.Crypto
{
    using System;
    using System.Numerics;

    internal static class DhUtil
    {
        public static readonly BigInteger MinPublicKey = new BigInteger(2);

        public static BigInteger MaxPublicKey(BigInteger p) => p - new BigInteger(2);

        public static bool IsValidPublicKey(BigInteger key, BigInteger p)
        {
            return key >= MinPublicKey && key <= MaxPublicKey(p);
        }

        public static byte[] PadToLength(byte[] input, int length)
        {
            if (input.Length >= length)
                return input;

            var padded = new byte[length];
            Buffer.BlockCopy(input, 0, padded, length - input.Length, input.Length);
            return padded;
        }

        public static byte[] Concat(params byte[][] segments)
        {
            var totalLength = segments.Sum(s => s.Length);
            var result = new byte[totalLength];

            var offset = 0;
            foreach (var segment in segments)
            {
                System.Buffer.BlockCopy(segment, 0, result, offset, segment.Length);
                offset += segment.Length;
            }
            
            return result;
        }
    }
}
