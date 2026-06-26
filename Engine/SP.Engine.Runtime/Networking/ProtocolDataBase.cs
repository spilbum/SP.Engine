using System;
using System.Reflection;
using SP.Core.Accessor;
using SP.Core.Serialization;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Networking
{
    public abstract class ProtocolDataBase<T> : IProtocolData
        where T : ProtocolDataBase<T>, new()
    {
        [Member(IgnoreGet = true)] public ushort Id { get; }
        [Member(IgnoreGet = true)] public ChannelKind Channel { get; }

        protected ProtocolDataBase()
        {
            var type = typeof(T);
            var attr = type.GetCustomAttribute<ProtocolDataAttribute>();
            if (attr == null)
                throw new InvalidOperationException($"[{type.FullName}] requires ProtocolDataAttribute");

            Id = attr.Id;
            Channel = attr.Channel;
        }

        public void Serialize(ref NetWriter w) => NetObject<T>.Serialize(ref w, (T)this);
        public void Deserialize(ref NetReader r) => NetObject<T>.Deserialize(ref r, (T)this);
    }
}
