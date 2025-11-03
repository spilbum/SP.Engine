using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolData
    {
        ushort ProtocolId { get; }
        ChannelKind Channel { get; }
    }
}
