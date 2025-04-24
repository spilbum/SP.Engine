using System;

namespace SP.Engine.Core.Security
{
    using System.Numerics;

    public enum DhKeySize : byte
    {
        Bit512 = 0x01,
        Bit2048 = 0x02
    }

    internal class DhParameters
    {
        public BigInteger P { get; }
        public BigInteger G { get; }

        private DhParameters(BigInteger p, BigInteger g)
        {
            P = p;
            G = g;
        }

        public int PublicKeyByteLength => (GetBitLength(P) + 7) / 8;

        public static DhParameters From(DhKeySize keySize)
        {
            return keySize switch
            {
                DhKeySize.Bit512 => new DhParameters(
                    BigInteger.Parse("FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1"
                                     + "29024E088A67CC74020BBEA63B139B22514A08798E3404DD"
                                     + "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245"
                                     + "E485B576625E7EC6F44C42E9A63A36210000000000090563", System.Globalization.NumberStyles.HexNumber),
                    new BigInteger(2)),
                DhKeySize.Bit2048 => new DhParameters(
                    BigInteger.Parse("FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E08"
                                     + "8A67CC74020BBEA63B139B22514A08798E3404DD"
                                     + "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6"
                                     + "D51C245E485B576625E7EC6F44C42E9A63A362100"
                                     + "00000000090563", System.Globalization.NumberStyles.HexNumber),
                    new BigInteger(2)),
                _ => throw new NotSupportedException($"Unsupported DH strength: {keySize}"),
            };
        }
        
        private static int GetBitLength(BigInteger value)
        {
            var bits = 0;
            while (value > 0)
            {
                value >>= 1;
                bits++;
            }
            return bits;
        }
    }
}
