using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocol
    {
        ushort Id { get; }
        ChannelKind Channel { get; }
    }
}
