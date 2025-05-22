using System;
using System.Numerics;
using System.Security.Cryptography;

namespace SP.Engine.Runtime.Security
{
    public class DiffieHellman
    {
        private readonly DhPrivateKey _privateKey;
        private readonly DhPublicKey _publicKey;
        private readonly DhParameters _parameters;

        public DhKeySize KeySize { get; }
        public byte[] SharedKey { get; private set; }

        public DiffieHellman(DhKeySize keySize)
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

        public void DeriveSharedKey(byte[] peerPublicKey)
        {
            var peerKey = new BigInteger(peerPublicKey, isBigEndian: true, isUnsigned: true);
            if (!DhUtil.IsValidPublicKey(peerKey, _parameters.P))
                throw new ArgumentException("Invalid peer public key. Key is out of bounds for DH parameter P.");

            var sharedSecret = BigInteger.ModPow(peerKey, _privateKey.KeyValue, _parameters.P);
            var sharedBytes = sharedSecret.ToByteArray(isUnsigned: true, isBigEndian: true);

            using var sha512 = SHA256.Create();
            SharedKey = sha512.ComputeHash(sharedBytes);
        }
    }
}
