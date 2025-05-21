using System;
using System.Numerics;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    internal static class DhUtil
    {
        public const int HmacSize = 32;
        
        private static readonly BigInteger MinPublicKey = new BigInteger(2);
        private static BigInteger MaxPublicKey(BigInteger p) => p - new BigInteger(2);

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
        
        public static byte[] ComputeHmac(byte[] key, byte[] message)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(message);
        }

        public static bool VerifyHmac(byte[] key, byte[] message, byte[] expectedHmac)
        {
            var computed = ComputeHmac(key, message);
            return CryptographicOperations.FixedTimeEquals(computed, expectedHmac);
        }
    }
}
