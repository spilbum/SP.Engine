using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Message
{
    public interface IMessage
    {
        long SequenceNumber { get; }
        EProtocolId ProtocolId { get; }
        byte[] ToArray();
        void SetSequenceNumber(long sequenceNumber);
        IProtocolData DeserializeProtocol(Type type, byte[] sharedKey = null);
    }
}
