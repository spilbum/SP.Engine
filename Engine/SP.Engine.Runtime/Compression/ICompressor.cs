using System;

namespace SP.Engine.Runtime.Compression
{
    public interface ICompressor
    {
        // 압축 시 필요한 최대 버퍼 크기 계산
        int GetMaxCompressedLength(int inputSize);
        // 압축 해제 후 원본 크기를 헤더 등에서 미리 알 수 있다고 가정 (Lz4는 헤더에 저장함)
        int GetDecompressedLength(ReadOnlySpan<byte> source);

        int Compress(ReadOnlySpan<byte> source, Span<byte> destination);
        int Decompress(ReadOnlySpan<byte> source, Span<byte> destination);
    }
}
