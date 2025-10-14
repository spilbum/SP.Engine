
using System;
using System.Buffers;
using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace SP.Engine.Runtime.Compression
{
    public class Lz4Compressor : ICompressor
    {
        private const int HeaderSize = 4;

        public byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentNullException(nameof(data));
            
            var maxCompressedSize = LZ4Codec.MaximumOutputSize(data.Length);  
            var buffer = ArrayPool<byte>.Shared.Rent(HeaderSize + maxCompressedSize);
            var target = buffer.AsSpan();
            
            try
            {
                // 원본 길이 저장
                var original = (uint)data.Length;
                BinaryPrimitives.WriteUInt32BigEndian(target, original);

                // 압축
                var compressedSize = LZ4Codec.Encode(data, target[HeaderSize..]);

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
        
        public byte[] Decompress(byte[] data)
        {
            if (data == null || data.Length < HeaderSize)
                throw new ArgumentException("Invalid compressed data", nameof(data));

            var span = new ReadOnlySpan<byte>(data);
            var original = BinaryPrimitives.ReadUInt32BigEndian(span[..HeaderSize]);
            if (original <= 0)
                throw new InvalidOperationException("Invalid original data length.");
            
            var result = new byte[original];
            var target = result.AsSpan();

            var decodedSize = LZ4Codec.Decode(span[HeaderSize..], target);
            if (decodedSize != original)
                throw new InvalidOperationException($"Decompressed size mismatch. decodedSize={decodedSize}, original={original}");
            
            return result;
        }
    }

}
