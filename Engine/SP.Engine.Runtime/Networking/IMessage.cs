using System;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Networking
{
    public interface IMessage : IDisposable
    {
        ushort Id { get; }
        void Retain();
        void Deserialize<TProtocol>(TProtocol instance, IEncryptor encryptor, ICompressor compressor) 
            where TProtocol : class, IProtocolData, new();
    }
}
