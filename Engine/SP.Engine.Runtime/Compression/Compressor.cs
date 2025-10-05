
using System;
using System.Buffers;
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
        private const int HeaderSize = sizeof(int);

        public byte[] Compress(byte[] source)
        {
            if (source == null || source.Length == 0)
                throw new ArgumentException("Source cannot be null or empty.", nameof(source));
            
            var maxCompressedSize = LZ4Codec.MaximumOutputSize(source.Length);  
            var buffer = ArrayPool<byte>.Shared.Rent(HeaderSize + maxCompressedSize);

            try
            {
                var span = buffer.AsSpan();
                // 원본 길이 저장
                span.WriteInt32(0, source.Length);

                // 압축
                var compressedSize = LZ4Codec.Encode(
                    source.AsSpan(),
                    span[HeaderSize..]
                );

                var totalSize = HeaderSize + compressedSize;
                
                // 실제 압축 결과 저장
                var result = new byte[totalSize];
                span[..totalSize].CopyTo(result);
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

            var span = (ReadOnlySpan<byte>)source.AsSpan();
            var originalLength = span.ReadInt32(0);
            if (originalLength <= 0)
                throw new InvalidOperationException("Invalid original data length.");
            
            var result = new byte[originalLength];

            var decodedSize = LZ4Codec.Decode(
                span[HeaderSize..],
                result
            );
            
            if (decodedSize != originalLength)
                throw new InvalidOperationException("Decompressed size mismatch.");
            
            return result;
        }
    }

}
