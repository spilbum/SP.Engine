using SP.Core.Logging;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Runtime.Command
{
    public interface ICommandContext : ILogContext
    {
        IEncryptor Encryptor { get; }
        ICompressor Compressor { get; }
    }
}
