using System;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public interface IMessage
    {
        long SequenceNumber { get; }
        ushort Id { get; }
        IProtocol Deserialize(Type type, IEncryptor encryptor);
    }
}
