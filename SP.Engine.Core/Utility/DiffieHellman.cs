using System;
using System.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace SP.Engine.Core.Utility
{

    public enum ECryptographicKeySize : byte
    {
        KS1024 = 0,
        KS512 = 1,
        KS256 = 2,
    }

    public class DiffieHellman
    {
        private static readonly BigInteger P1024 = new BigInteger(
            "d6549c5c901f8b901fbaed10b9357920359fefc6f340ed159546628061c80aef392dce0995eff429d326fa13f05635da6266c3341d1afc8b76af82dfcee73c4b" +
            "54acf208b58da0694a88b8d8a493a89a6b5303e1f1081154d5402d7b0e910287c2c1b4fe1fcc0ca2d743a50feb109789a1046bc7e4aa2af3684533ce633ebf33",
            16);

        private static readonly BigInteger G1024 = new BigInteger(
            "457a35bcd534d05903f87d8b9b93798c5036cab0789410119787c007c02393d4e6e663f060f22babd5cfe035b04595e9c186a9b68ca3a4a0210ef331f8d55960" +
            "4973dad6107f5aa65fd72183fb4f14ed0a7b38bf3cdd0475f5be33a8e6ce7b6558774c40b286a81248d547b75c99d76acf562c33372a133c9ec703c420eb3cea",
            16);

        private static readonly BigInteger P512 = new BigInteger(
            "f039233b2eb875855050079d6c4bc34b5732c711b59380742bf3e83bafd8d706252ffa62f709bd1282c02ff111d0fae4847141c55b2ca9ae861d334124d2463f",
            16);

        private static readonly BigInteger G512 = new BigInteger(
            "a4b2d271d61cb03e642707e0314a7cb36cd7e342177a1daec5126abc6338501ac059cb835e60fef7b55a193b53d047e05d40839351a8b97411172597468fc0aa",
            16);

        private static readonly BigInteger P256 = new BigInteger(
            "a90ccd06d1d7c2d14cfcef2fe2821149647395922404bd5e57057d9195abfe5b", 16);

        private static readonly BigInteger G256 = new BigInteger(
            "633bff1242955e490ea52942d9042df0cfbbd63d18ebabc301c2b7d5fc638acc", 16);



        /// <summary>
        /// 키교환에 사용하는 공개 키
        /// </summary>
        public byte[] PublicKey =>
            SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(_cipher.Public).GetDerEncoded();

        /// <summary>
        /// 서버와 클라이언트 간 암호화/복호화에 사용하는 대칭 키
        /// </summary>
        public byte[] SharedKey { get; private set; }
        public ECryptographicKeySize KeySize { get; private set; }

        /// <summary>
        /// 디피헬만 키 사이즈
        /// </summary>
        private readonly int _keySizeValue;

        private readonly AsymmetricCipherKeyPair _cipher;

        public DiffieHellman(ECryptographicKeySize keySize)
        {
            BigInteger prime;
            BigInteger generator;
            switch (keySize)
            {
                case ECryptographicKeySize.KS1024:
                {
                    prime = P1024;
                    generator = G1024;
                    break;
                }
                case ECryptographicKeySize.KS512:
                {
                    prime = P512;
                    generator = G512;
                    break;
                }
                case ECryptographicKeySize.KS256:
                {
                    prime = P256;
                    generator = G256;
                    break;
                }
                default:
                {
                    throw new ArgumentException($"Invalid key size: {keySize}");
                }
            }

            KeySize = keySize;

            var keyGen = GeneratorUtilities.GetKeyPairGenerator("DH");
            var kgp = new DHKeyGenerationParameters(new SecureRandom(), new DHParameters(prime, generator));
            keyGen.Init(kgp);
            _cipher = keyGen.GenerateKeyPair();
            _keySizeValue = Convert.ToInt32(keySize.ToString().Replace("KS", ""));
        }

        /// <summary>
        /// 대칭 키를 생성합니다.
        /// </summary>
        /// <param name="outherPartyPublicKey"></param>
        public void DeriveSharedKey(byte[] outherPartyPublicKey)
        {
            if (outherPartyPublicKey == null || outherPartyPublicKey.Length == 0)
                throw new ArgumentException("Public key is null or empty");

            var keyAgree = AgreementUtilities.GetBasicAgreement("DH");
            keyAgree.Init(_cipher.Private);

            try
            {
                var publicKey = PublicKeyFactory.CreateKey(outherPartyPublicKey);
                var k = keyAgree.CalculateAgreement(publicKey);

                // AES-256 
                var length = 32;

                switch (_keySizeValue)
                {
                    case 256:
                    {
                        // AES-192
                        length = 24;
                        break;
                    }
                }

                SharedKey = k.ToByteArrayUnsigned().Take(length).ToArray();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"An exception occurred: {e.Message}\r\nstackTrace={e.StackTrace}");
            }
        }
    }
}
