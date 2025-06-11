using System;
using System.Buffers;
using SP.Engine.Runtime.Message;

namespace SP.Engine.Client
{
    public readonly struct PooledSegment : IDisposable
    {
        public ArraySegment<byte> Segment { get; }
        public int Length => Segment.Count;

        private PooledSegment(ArraySegment<byte> segment)
        {
            Segment = segment; 
        }

        public void Dispose()
        {
            if (Segment.Array != null)
                ArrayPool<byte>.Shared.Return(Segment.Array);
        }

        public static PooledSegment FromMessage(IMessage message)
        {
            var length = message.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            message.WriteTo(buffer.AsSpan(0, length));
            return new PooledSegment(new ArraySegment<byte>(buffer, 0, length));
        }
        
        public static PooledSegment FromFragment(UdpFragment fragment)
        {
            var length = fragment.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            fragment.WriteTo(buffer.AsSpan());
            return new PooledSegment(new ArraySegment<byte>(buffer, 0, length));
        }
    }
}
