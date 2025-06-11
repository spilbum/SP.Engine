using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Message
{
    public interface IMessage
    {
        long SequenceNumber { get; }
        EProtocolId ProtocolId { get; }
        int Length { get; }
        void WriteTo(Span<byte> buffer);
        IProtocolData Unpack(Type type, byte[] sharedKey);
    }
}
