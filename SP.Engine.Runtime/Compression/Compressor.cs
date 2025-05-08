
using System;
using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace SP.Engine.Runtime.Compression
{
    public static class Compressor
    {
        private const int HeaderSize = sizeof(int); // Original length only

        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("data");

            var originalLength = data.Length;
            var maxOutputSize = LZ4Codec.MaximumOutputSize(originalLength);

            byte[] buffer = new byte[HeaderSize + maxOutputSize];
            var span = buffer.AsSpan();

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, sizeof(int)), originalLength);

            var compressedSize = LZ4Codec.Encode(
                data, 0, originalLength,
                buffer, HeaderSize, maxOutputSize
            );

            Array.Resize(ref buffer, HeaderSize + compressedSize);
            return buffer;
        }

        public static byte[] Decompress(byte[] data)
        {
            if (data == null || data.Length <= HeaderSize)
                throw new ArgumentException("data");

            var span = data.AsSpan();
            var originalLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, sizeof(int)));

            var decompressedData = new byte[originalLength];
            var decodedSize = LZ4Codec.Decode(
                data, HeaderSize, data.Length - HeaderSize,
                decompressedData, 0, originalLength
            );

            if (decodedSize != originalLength)
                throw new InvalidOperationException("Decompressed size mismatch");

            return decompressedData;
        }
    }

}
