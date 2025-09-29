using System;
using System.Collections.Concurrent;
using System.Reflection;
using SP.Common.Accessor;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public abstract class BaseProtocol : IProtocol
    {
        private static readonly ConcurrentDictionary<Type, ProtocolAttribute> Cache =
            new ConcurrentDictionary<Type, ProtocolAttribute>();
        
        [MemberIgnore] public ushort Id { get; }
        [MemberIgnore] public ChannelKind Channel { get; }

        protected BaseProtocol()
        {
            var type = GetType();
            var attr = Cache.GetOrAdd(type, t =>
            {
                var a = t.GetCustomAttribute<ProtocolAttribute>();
                if (a == null)
                    throw new InvalidOperationException($"[{t.FullName}] requires {nameof(ProtocolAttribute)}");
                return a;
            });
            Id = attr.Id;
            Channel = attr.Channel;
        }
    }
}
