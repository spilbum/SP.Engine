using SP.Core.Accessor;
using SP.Core.Serialization;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public abstract class BaseProtocolData<T> : IProtocolData
        where T : BaseProtocolData<T>
    {
        [Member(IgnoreGet = true)] public ushort Id => ProtocolMetadata<T>.Id;
        [Member(IgnoreGet = true)] public ChannelKind Channel => ProtocolMetadata<T>.Channel;

        public void Serialize(NetWriter w)
        {
            NetSerializer<T>.Serialize(ref w, (T)this);
        }
    }
}
