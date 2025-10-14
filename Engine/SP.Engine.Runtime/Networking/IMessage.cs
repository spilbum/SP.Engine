using System;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public interface IMessage
    {
        ushort Id { get; }
        TProtocol Deserialize<TProtocol>(IEncryptor encryptor, ICompressor compressor) where TProtocol : IProtocolData;
    }
}
