using SP.Core.Logging;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolHandlerContext : ILogContext
    {
        IEncryptor Encryptor { get; }
        ICompressor Compressor { get; }
    }
}
