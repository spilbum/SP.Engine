using System;

namespace SP.Core.Buffers
{
    public interface IBufferResizer
    {
        Span<byte> Resize(int size, int position);
    }
}
