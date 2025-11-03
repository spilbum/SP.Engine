using SP.Core.Logging;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public interface ICommandContext : ILogContext
    {
        TProtocol Deserialize<TProtocol>(IMessage message) where TProtocol : IProtocolData;
    }
}
