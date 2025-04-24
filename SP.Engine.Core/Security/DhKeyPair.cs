namespace SP.Engine.Core.Security
{
    using System;
    using System.Numerics;
    using System.Security.Cryptography;

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
            var byteLength = parameters.PublicKeyByteLength;
            var bytes = new byte[byteLength];

            BigInteger priv;
            do
            {
                RandomNumberGenerator.Fill(bytes);
                priv = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
            } while (priv < DhUtil.MinPublicKey || priv >= DhUtil.MaxPublicKey(parameters.P));

            var pub = BigInteger.ModPow(parameters.G, priv, parameters.P);
            return new DhKeyPair(
                new DhPrivateKey(priv, parameters),
                new DhPublicKey(pub, parameters));
        }
    }
}
