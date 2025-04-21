using System;
using SP.Engine.Core.Protocol;

namespace SP.Engine.Core.Message
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

    [Flags]
    internal enum EMessageFlags : byte
    {
        None = 0,
        Encrypted = 1 << 0,  // 암호화됨
        Compressed = 1 << 1, // 압축됨
    }
}
