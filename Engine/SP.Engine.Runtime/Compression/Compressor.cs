
using System;
using System.Buffers;
using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace SP.Engine.Runtime.Compression
{
    public interface ICompressor
    {
        byte[] Compress(byte[] data);
        byte[] Decompress(byte[] data);
    }
    
    public class Lz4Compressor : ICompressor
    {
        private const int HeaderSize = 4;

        public byte[] Compress(byte[] source)
        {
            if (source == null || source.Length == 0)
                throw new ArgumentException("Source cannot be null or empty.", nameof(source));
            
            var maxCompressedSize = LZ4Codec.MaximumOutputSize(source.Length);  
            var buffer = ArrayPool<byte>.Shared.Rent(HeaderSize + maxCompressedSize);
            var target = buffer.AsSpan();
            
            try
            {
                // 원본 길이 저장
                BinaryPrimitives.WriteInt32BigEndian(target, source.Length);
                Console.WriteLine($"[Compress] original: {source.Length}");

                // 압축
                var compressedSize = LZ4Codec.Encode(source, target[HeaderSize..]);

                // 실제 압축 크기
                var totalSize = HeaderSize + compressedSize;
                
                // 실제 압축 결과 저장
                var result = new byte[totalSize];
                var dst = result.AsSpan();
                target[..totalSize].CopyTo(dst);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        
        public byte[] Decompress(byte[] source)
        {
            if (source == null || source.Length < HeaderSize)
                throw new ArgumentException("Invalid compressed data", nameof(source));

            var original = BinaryPrimitives.ReadInt32BigEndian(source);
            Console.WriteLine($"[Decompress] original: {original}");
            if (original <= 0)
                throw new InvalidOperationException("Invalid original data length.");
            
            var result = new byte[original];
            var target = result.AsSpan();

            var decodedSize = LZ4Codec.Decode(source[HeaderSize..], target);
            if (decodedSize != original)
                throw new InvalidOperationException($"Decompressed size mismatch. decodedSize={decodedSize}, original={original}");
            
            return result;
        }
    }

}
