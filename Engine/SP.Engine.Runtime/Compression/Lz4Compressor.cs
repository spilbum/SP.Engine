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
            var originalLength = source.ReadInt32(0);

            if (originalLength > _maxDecompressedSize)
            {
                throw new InvalidDataException(
                    $"Lz4 decompressed size exceeded limit: {originalLength} > {_maxDecompressedSize}");
            }
            
            return originalLength;
        }

        public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            // 1. 원본 길이 기록 (헤더)
            destination.WriteInt32(0, source.Length);
        
            // 2. 데이터 압축 (Zero Allocation)
            var compressedBytes = LZ4Codec.Encode(source, destination[HeaderSize..], _level);
        
            return HeaderSize + compressedBytes;
        }

        public int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var originalLength = source.ReadInt32(0);
            if (destination.Length < originalLength)
                throw new ArgumentException($"Destination buffer too small. Required: {originalLength}, Actual: {destination.Length}");

            var decoded = LZ4Codec.Decode(source[HeaderSize..], destination);
            if (decoded != originalLength)
                throw new InvalidDataException("Decompressed size mismatch");
            
            return decoded;
        }
    }
}
