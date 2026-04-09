using System;
using System.Reflection;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    internal static class ProtocolMetadata<T> where T : IProtocolData
    {
        public static readonly ushort Id;
        public static readonly ChannelKind Channel;

        static ProtocolMetadata()
        {
            var type = typeof(T);
            var attr = type.GetCustomAttribute<ProtocolAttribute>();
            if (attr == null)
                throw new InvalidOperationException($"[{type.FullName}] requires ProtocolAttribute");

            Id = attr.Id;
            Channel = attr.Channel;
        }
    }
}
