using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Message
{
    public interface IMessage
    {
        long SequenceNumber { get; }
        EProtocolId ProtocolId { get; }
        int Length { get; }
        void Pack(IProtocolData data, byte[] sharedKey, PackOptions options);
        IProtocolData Unpack(Type type, byte[] sharedKey);
    }
}
