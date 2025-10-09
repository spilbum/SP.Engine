using System;
using SP.Engine.Runtime.Serialization;

namespace SP.Engine.Runtime.Networking
{
    public interface IHeader
    {
        HeaderFlags Flags { get; }
        ushort Id { get; }
        int Size { get; }
        void WriteTo(Span<byte> s);
    }

    [Flags]
    public enum FrameFlags : byte
    {
        None = 0,
        Encrypted = 1 << 0,
        Compressed = 1 << 1,
    }

    public readonly struct FrameHeader
    {
        public const byte Version = 1;
        public readonly FrameFlags Flags;
        public readonly ulong Seq;
        public readonly ushort MsgId;
        public readonly int PayloadLength;

        public FrameHeader(FrameFlags flags, ulong seq, ushort msgId, int payloadLength)
        {
            Flags = flags;
            Seq = seq;
            MsgId = msgId;
            PayloadLength = payloadLength;
        }
        
        
    }
}
