using SP.Core.Serialization;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public interface IProtocolData : INetSerializable
    {
        ushort Id { get; }
        ChannelKind Channel { get; }
    }
}
