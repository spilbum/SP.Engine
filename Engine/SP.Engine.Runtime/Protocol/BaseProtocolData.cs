using System;
using System.Collections.Concurrent;
using System.Reflection;
using SP.Common.Accessor;

namespace SP.Engine.Runtime.Protocol
{
    public abstract class BaseProtocolData : IProtocolData
    {
        private static readonly ConcurrentDictionary<Type, ProtocolDataAttribute> Cache = new ConcurrentDictionary<Type, ProtocolDataAttribute>();
        
        [IgnoreMember]
        public EProtocolId ProtocolId { get; }
        [IgnoreMember]
        public EProtocolType ProtocolType { get; }
        
        protected BaseProtocolData()
        {
            var type = GetType();
            if (!Cache.TryGetValue(type, out var attr))
            {
                attr = type.GetCustomAttribute<ProtocolDataAttribute>()
                           ?? throw new InvalidCastException($"Invalid protocol data attribute: {type.FullName}");
                Cache[type] = attr;
            }
            
            ProtocolId = attr.ProtocolId;
            ProtocolType = attr.ProtocolType;
        }
    }

}
