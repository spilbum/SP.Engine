using System;
using SP.Engine.Core.Protocol;

namespace SP.Engine.Core.Message
{
    public interface IMessage
    {
        long SequenceNumber { get; }
        EProtocolId ProtocolId { get; }
        byte[] ToArray();
        bool ReadBuffer(Buffer buffer);
        void SetSequenceNumber(long sequenceNumber);
        void SerializeProtocol(IProtocolData protocol, byte[] sharedKey);
        IProtocolData DeserializeProtocol(Type type, byte[] sharedKey);
    }

    [Flags]
    public enum EOption : byte
    {
        None = 0,
        Encrypt = 1,
        Compress = 2,
        All = Encrypt | Compress
    }
}
