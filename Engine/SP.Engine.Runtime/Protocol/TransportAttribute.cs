using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TransportAttribute : Attribute
    {
        public ETransport Transport { get; }

        public TransportAttribute(ETransport transport)
        {
            Transport = transport;
        }
    }

    public static class TransportHelper
    {
        private static readonly ConcurrentDictionary<Type, TransportAttribute> Cache = new ConcurrentDictionary<Type, TransportAttribute>();

        public static TransportAttribute GetAttribute(Type type)
        {
            if (Cache.TryGetValue(type, out var attr))
                return attr;

            attr = type.GetCustomAttribute<TransportAttribute>(false);
            Cache[type] = attr;
            return attr;
        }
        
        public static TransportAttribute GetAttribute(IProtocolData data) 
            => GetAttribute(data.GetType());

        public static ETransport Resolve(IProtocolData data)
            => GetAttribute(data)?.Transport ?? ETransport.Tcp;
    }
}
