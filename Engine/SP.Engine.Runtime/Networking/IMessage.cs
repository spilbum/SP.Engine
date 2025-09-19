using System;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public interface IMessage
    {
        long SequenceNumber { get; }
        EProtocolId ProtocolId { get; }
        int Length { get; }
        void Pack(IProtocolData data, IEncryptor encryptor, PackOptions options);
        IProtocolData Unpack(Type type, IEncryptor encryptor);
    }
}
