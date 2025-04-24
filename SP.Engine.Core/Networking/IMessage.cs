using System;
using SP.Engine.Core.Protocols;

namespace SP.Engine.Core.Networking
{
    public interface IMessage
    {
        long SequenceNumber { get; }
        EProtocolId ProtocolId { get; }
        byte[] ToArray();
        void SetSequenceNumber(long sequenceNumber);
        void SerializeProtocol(IProtocolData protocol, byte[] sharedKey);
        IProtocolData DeserializeProtocol(Type type, byte[] sharedKey);
    }


}
