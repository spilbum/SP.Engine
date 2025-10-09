using System;
using System.Numerics;

namespace SP.Engine.Runtime.Security
{
    public enum DhKeySize : ushort
    {
        Test512 = 512,
        Bit2048 = 2048
    }

    internal class DhParameters
    {
        public BigInteger P { get; }
        public BigInteger Q { get; } // (p-1)/2 또는 소인수 q
        public BigInteger G { get; }
        public int PublicKeyByteLength { get; }

        private DhParameters(BigInteger p, BigInteger q, BigInteger g)
        {
            P = p;
            Q = q;
            G = g;
            // 바이트 길이: p 비트 길이에 맞춘 바이트 길이
            PublicKeyByteLength = (p.GetBitLength() + 7) / 8;
        }

        public static DhParameters From(DhKeySize keySize)
        {
            switch (keySize)
            {
                case DhKeySize.Test512:
                    const string pTestHex = "F7E75FDC469067FFDC4E847C51F452DF"; 
                    var p = BigInteger.Parse("0" + pTestHex, System.Globalization.NumberStyles.HexNumber);
                    var q = (p - BigInteger.One) / 2;
                    var g = new BigInteger(2);
                    return new DhParameters(p, q, g);

                case DhKeySize.Bit2048:
                    // RFC 3526 - 2048-bit MODP Group (group 14)
                    // Hex is the RFC3526 2048-bit prime expressed as a continuous hex string.
                    const string pHex = "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
                                        "29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
                                        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
                                        "E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED" +
                                        "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3D" +
                                        "C2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F" +
                                        "83655D23DCA3AD961C62F356208552BB9ED529077096966D" +
                                        "670C354E4ABC9804F1746C08CA237327FFFFFFFFFFFFFFFF";

                    // Note: some representations include slightly different spacing/linebreaks;
                    // above string is the RFC3526 2048-bit prime concatenated.
                    var pVal = BigInteger.Parse("0" + pHex, System.Globalization.NumberStyles.HexNumber);

                    // For safe-prime groups, q = (p - 1) / 2
                    var qVal = (pVal - BigInteger.One) / 2;

                    var gVal = new BigInteger(2);

                    return new DhParameters(pVal, qVal, gVal);

                default:
                    throw new ArgumentOutOfRangeException(nameof(keySize));
            }
        }
    }
}
