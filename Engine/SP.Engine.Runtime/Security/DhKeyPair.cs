using System.Numerics;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    public static class BigIntegerExtensions
    {
        public static int GetBitLength(this BigInteger value)
        {
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length == 0)
                return 0;

            int leading = 8;
            byte first = bytes[0];

            while ((first & 0x80) == 0)
            {
                first <<= 1;
                leading--;
            }

            return (bytes.Length - 1) * 8 + leading;
        }
    }
    
    internal class DhPrivateKey
    {
        public DhPrivateKey(BigInteger keyValue, DhParameters parameters)
        {
            KeyValue = keyValue;
            Parameters = parameters;
        }

        public BigInteger KeyValue { get; set; }
        public DhParameters Parameters { get; set; }
    }

    internal class DhPublicKey
    {
        public DhPublicKey(BigInteger keyValue, DhParameters parameters)
        {
            KeyValue = keyValue;
            Parameters = parameters;
        }
        
        public BigInteger KeyValue { get; set; }
        public DhParameters Parameters { get; set; }
    }

    internal class DhKeyPair
    {
        public DhPrivateKey PrivateKey { get; }
        public DhPublicKey PublicKey { get; }

        private DhKeyPair(DhPrivateKey priv, DhPublicKey pub)
        {
            PrivateKey = priv;
            PublicKey = pub;
        }

        public static DhKeyPair Generate(DhParameters parameters)
        {
            var p = parameters.P;
            var g = parameters.G;

            var bitLength = p.GetBitLength();
            var byteLength = (bitLength + 7) / 8;

            var bytes = new byte[byteLength];
            int extraBits = byteLength * 8 - bitLength;
            byte mask = (byte)(0xFF >> extraBits);

            BigInteger priv;
            do
            {
                RandomNumberGenerator.Fill(bytes);
                bytes[0] &= mask;
                priv = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
            } while (!DhUtil.IsValidPublicKey(priv, p));

            var pub = BigInteger.ModPow(g, priv, p);
            return new DhKeyPair(
                new DhPrivateKey(priv, parameters),
                new DhPublicKey(pub, parameters));
        }
    }
}
