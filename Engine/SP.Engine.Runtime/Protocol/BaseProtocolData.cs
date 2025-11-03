using System;
using System.Collections.Concurrent;
using System.Reflection;
using SP.Core.Accessor;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public abstract class BaseProtocolData : IProtocolData
    {
        private static readonly ConcurrentDictionary<Type, ProtocolAttribute> Cache =
            new ConcurrentDictionary<Type, ProtocolAttribute>();

        protected BaseProtocolData()
        {
            var type = GetType();
            var attr = Cache.GetOrAdd(type, t =>
            {
                var a = t.GetCustomAttribute<ProtocolAttribute>();
                if (a == null)
                    throw new InvalidOperationException($"[{t.FullName}] requires {nameof(ProtocolAttribute)}");
                return a;
            });
            ProtocolId = attr.ProtocolId;
            Channel = attr.Channel;
        }

        [Member(IgnoreGet = true)] public ushort ProtocolId { get; }
        [Member(IgnoreGet = true)] public ChannelKind Channel { get; }
    }
}
