using SP.Common.Logging;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    public interface ICommandContext
    {
        ILogger Logger { get; }
        TProtocol Deserialize<TProtocol>(IMessage message) where TProtocol : IProtocol;
    }
}
