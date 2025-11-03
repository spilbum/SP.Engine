using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using K4os.Compression.LZ4;

namespace SP.Engine.Runtime.Compression
{
    public class Lz4Compressor : ICompressor
    {
        private const int HeaderSize = 4;
        private readonly LZ4Level _level;
        private readonly int _maxDecompressedSize;

        public Lz4Compressor(int maxDecompressedBytes, LZ4Level level = LZ4Level.L00_FAST)
        {
            if (maxDecompressedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxDecompressedBytes));
            _maxDecompressedSize = maxDecompressedBytes;
            _level = level;
        }

        public byte[] Compress(byte[] data)
        {
            return Compress((ReadOnlySpan<byte>)data);
        }

        public byte[] Decompress(byte[] data)
        {
            return Decompress((ReadOnlySpan<byte>)data);
        }

        public byte[] Compress(ReadOnlySpan<byte> data)
        {
            var originalLen = (uint)data.Length;
            var maxCompressed = data.Length == 0 ? 0 : CompressBound(data.Length);

            var rented = ArrayPool<byte>.Shared.Rent(HeaderSize + maxCompressed);

            try
            {
                var target = rented.AsSpan();
                // 원본 길이 저장
                BinaryPrimitives.WriteUInt32BigEndian(target, originalLen);

                var compressedSize = 0;
                if (data.Length > 0)
                {
                    compressedSize = LZ4Codec.Encode(data, target.Slice(HeaderSize, maxCompressed), _level);
                    if (compressedSize <= 0)
                        throw new InvalidDataException("Lz4 encode failed (destination too small or other error).");
                }

                var total = HeaderSize + compressedSize;
                var result = new byte[total];
                target[..total].CopyTo(result.AsSpan());
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public byte[] Decompress(ReadOnlySpan<byte> data)
        {
            if (data.Length < HeaderSize)
                throw new ArgumentException("Lz4 data too short (missing header).", nameof(data));

            var originalLen = BinaryPrimitives.ReadUInt32BigEndian(data[..HeaderSize]);
            if (originalLen > (uint)_maxDecompressedSize)
                throw new InvalidDataException(
                    $"Lz4 original length too large: {originalLen:n0} > max {_maxDecompressedSize:n0}.");
            var payload = data[HeaderSize..];

            if (originalLen == 0)
            {
                if (payload.Length != 0)
                    throw new InvalidDataException("Invalid Lz4 frame for zero-length payload.");
                return Array.Empty<byte>();
            }

            var result = new byte[originalLen];
            var decoded = LZ4Codec.Decode(payload, result.AsSpan());
            if (decoded != originalLen)
                throw new InvalidDataException(
                    $"Decompressed size mismatch. decoded={decoded}, expected={originalLen}.");
            return result;
        }

        private static int CompressBound(int n)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            var bound = (long)n + n / 255 + 16;
            if (bound > int.MaxValue) throw new OutOfMemoryException("Lz4 compress bound overflow");
            return (int)bound;
        }
    }
}
