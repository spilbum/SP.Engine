
using System;
using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace SP.Engine.Runtime.Compression
{
    public static class Compressor
    {
        private const int HeaderSize = sizeof(int);
        
        public static byte[] Compress(byte[] source)
        {
            if (source == null || source.Length == 0)
                throw new ArgumentException("data");

            var originalLength = source.Length;
            var maxOutputSize = LZ4Codec.MaximumOutputSize(originalLength);

            var buffer = new byte[HeaderSize + maxOutputSize];
            var span = buffer.AsSpan();

            BinaryPrimitives.WriteInt32LittleEndian(span[..HeaderSize], originalLength);

            var compressedSize = LZ4Codec.Encode(
                source, 0, originalLength,
                buffer, HeaderSize, maxOutputSize
            );

            Array.Resize(ref buffer, HeaderSize + compressedSize);
            return buffer;
        }

        public static byte[] Decompress(byte[] source)
        {
            if (source == null || source.Length <= HeaderSize)
                throw new ArgumentException("data");

            var span = source.AsSpan();
            var originalLength = BinaryPrimitives.ReadInt32LittleEndian(span[..HeaderSize]);

            var decompressedData = new byte[originalLength];
            var decodedSize = LZ4Codec.Decode(
                source, HeaderSize, source.Length - HeaderSize,
                decompressedData, 0, originalLength
            );

            if (decodedSize != originalLength)
                throw new InvalidOperationException("Decompressed size mismatch");

            return decompressedData;
        }
    }

}
