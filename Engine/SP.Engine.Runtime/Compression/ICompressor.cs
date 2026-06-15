using System;

namespace SP.Engine.Runtime.Compression
{
    public interface ICompressor
    {
        // 압축 시 필요한 최대 버퍼 크기 계산
        int GetMaxCompressedLength(int inputSize);
        /// <summary>
        /// 원본 크기를 가져옴
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        int GetDecompressedLength(ReadOnlySpan<byte> source);

        int Compress(ReadOnlySpan<byte> source, Span<byte> destination);
        int Decompress(ReadOnlySpan<byte> source, Span<byte> destination);
    }
}
