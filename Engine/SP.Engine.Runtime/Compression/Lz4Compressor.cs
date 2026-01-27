using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using K4os.Compression.LZ4;

namespace SP.Engine.Runtime.Compression
{
    public class Lz4Compressor : ICompressor
    {
        private const int HeaderSize = 4; // 원본 길이 저장용
        private readonly LZ4Level _level;
        private readonly int _maxDecompressedSize;

        public Lz4Compressor(int maxDecompressedSize, LZ4Level level = LZ4Level.L00_FAST)
        {
            if (maxDecompressedSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDecompressedSize));
            
            _level = level;
            _maxDecompressedSize = maxDecompressedSize;
        }

        public int GetMaxCompressedLength(int inputSize) 
            => HeaderSize + LZ4Codec.MaximumOutputSize(inputSize);

        public int GetDecompressedLength(ReadOnlySpan<byte> source)
        {
            if (source.Length < HeaderSize) return 0;
            var originalLen = (int)BinaryPrimitives.ReadUInt32BigEndian(source[..HeaderSize]);

            if (originalLen > _maxDecompressedSize)
            {
                throw new InvalidDataException(
                    $"Lz4 decompressed size exceeded limit: {originalLen} > {_maxDecompressedSize}");
            }
            
            return originalLen;
        }

        public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            // 1. 원본 길이 기록 (헤더)
            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)source.Length);
        
            // 2. 데이터 압축 (Zero Allocation)
            var compressedBytes = LZ4Codec.Encode(source, destination[HeaderSize..], _level);
        
            return HeaderSize + compressedBytes;
        }

        public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var originalLen = (int)BinaryPrimitives.ReadUInt32BigEndian(source[..HeaderSize]);
            if (destination.Length < originalLen)
                throw new ArgumentException($"Destination buffer too small. Required: {originalLen}, Actual: {destination.Length}");

            var decoded = LZ4Codec.Decode(source[HeaderSize..], destination);
            if (decoded != originalLen)
                throw new InvalidDataException("Decompressed size mismatch");
            
            return decoded;
        }
    }
}
