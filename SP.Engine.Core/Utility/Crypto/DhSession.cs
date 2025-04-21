using System.Text;

namespace SP.Engine.Core.Utility.Crypto
{
    using System;
    using System.Numerics;
    using System.Security.Cryptography;

    public class DhSession
    {
        private readonly DhPrivateKey _privateKey;
        private readonly DhPublicKey _publicKey;
        private readonly DhParameters _parameters;

        public DhKeySize KeySize { get; }
        public byte[] SharedKey { get; private set; }
        public byte[] HmacKey { get; private set; }

        public DhSession(DhKeySize keySize)
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
                throw new ArgumentException("Invalid peer public key");

            var sharedSecret = BigInteger.ModPow(peerKey, _privateKey.KeyValue, _parameters.P);
            var sharedBytes = sharedSecret.ToByteArray(isUnsigned: true, isBigEndian: true);

            using var sha512 = SHA512.Create();
            var hash = sha512.ComputeHash(sharedBytes); // 64바이트

            SharedKey = new byte[32];
            HmacKey = new byte[32];
            Buffer.BlockCopy(hash, 0, SharedKey, 0, 32);
            Buffer.BlockCopy(hash, 32, HmacKey, 0, 32);
        }
    }
    
    public class SecureSession
    {
        public byte[] AesKey => _aesKey;
        public byte[] HmacKey => _hmacKey;

        private readonly byte[] _aesKey;
        private readonly byte[] _hmacKey;

        public SecureSession(byte[] aesKey, byte[] hmacKey)
        {
            if (aesKey.Length != 32 || hmacKey.Length != 32)
                throw new ArgumentException("Invalid AES or HMAC key size");

            _aesKey = aesKey;
            _hmacKey = hmacKey;
        }

        public static SecureSession FromDhSession(DhSession session)
            => new SecureSession(session.SharedKey, session.HmacKey);
    }
}
