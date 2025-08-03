using System;
using System.Numerics;

namespace SP.Engine.Runtime.Security
{
    public enum EDhKeySize : byte
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

        public static DhParameters From(EDhKeySize keySize)
        {
            return keySize switch
            {
                EDhKeySize.Bit512 => new DhParameters(
                    ParseUnsignedBigEndian("FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1"
                                           + "29024E088A67CC74020BBEA63B139B22514A08798E3404DD"
                                           + "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245"
                                           + "E485B576625E7EC6F44C42E9A63A36210000000000090563"),
                    new BigInteger(2)),
                EDhKeySize.Bit2048 => new DhParameters(
                    ParseUnsignedBigEndian("FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E08"
                                           + "8A67CC74020BBEA63B139B22514A08798E3404DD"
                                           + "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6"
                                           + "D51C245E485B576625E7EC6F44C42E9A63A362100"
                                           + "00000000090563"),
                    new BigInteger(2)),
                _ => throw new NotSupportedException($"Unsupported DH strength: {keySize}"),
            };
        }
        
        private static BigInteger ParseUnsignedBigEndian(string hex)
        {
            var bytes = HexToBytes(hex);
            return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        }
        
        private static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length");

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
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
