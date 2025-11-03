using System;
using System.Numerics;

namespace SP.Engine.Runtime.Security
{
    internal class DhPrivateKey
    {
        public DhPrivateKey(BigInteger keyValue)
        {
            KeyValue = keyValue;
        }

        public BigInteger KeyValue { get; }
    }

    internal class DhPublicKey
    {
        public DhPublicKey(BigInteger keyValue)
        {
            KeyValue = keyValue;
        }

        public BigInteger KeyValue { get; }
    }

    internal class DhKeyPair
    {
        private DhKeyPair(DhPrivateKey privateKey, DhPublicKey publicKey)
        {
            PrivateKey = privateKey;
            PublicKey = publicKey;
        }

        public DhPrivateKey PrivateKey { get; }
        public DhPublicKey PublicKey { get; }

        public static DhKeyPair Generate(DhParameters parameters)
        {
            if (parameters is null) throw new ArgumentNullException(nameof(parameters));
            if (parameters.Q <= BigInteger.One)
                throw new ArgumentException("Invalid Q in parameters", nameof(parameters));

            BigInteger x;
            var qMinusTwo = parameters.Q - 2;
            do
            {
                x = DhUtil.RandomBigIntegerLessThan(parameters.Q - 2) + 2;
            } while (x < 2 || x > qMinusTwo);

            var y = BigInteger.ModPow(parameters.G, x, parameters.P);
            return new DhKeyPair(new DhPrivateKey(x), new DhPublicKey(y));
        }
    }
}
