using System;

namespace SP.Engine.Runtime.Compression
{
    public interface ICompressor
    {
        byte[] Compress(ReadOnlySpan<byte> data);
        byte[] Decompress(ReadOnlySpan<byte> data);

        byte[] Compress(byte[] data);
        byte[] Decompress(byte[] data);
    }
}
