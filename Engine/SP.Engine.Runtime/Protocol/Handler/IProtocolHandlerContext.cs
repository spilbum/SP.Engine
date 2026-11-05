using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolHandlerContext
    {
        IEncryptor Encryptor { get; }
        ICompressor Compressor { get; }
    }
}
