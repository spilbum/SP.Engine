using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolData
    {
        ushort Id { get; }
        ChannelKind Channel { get; }
    }
}
