using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolHandler
    {
        string Name { get; }
        long Execute(IProtocolHandlerContext context, IMessage message);
    }
}
