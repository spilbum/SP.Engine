using System;
using System.Collections.Concurrent;
using System.Reflection;
using SP.Common.Accessor;

namespace SP.Engine.Runtime.Protocol
{
    public abstract class BaseProtocolData : IProtocolData
    {
        private static readonly ConcurrentDictionary<Type, ProtocolDataAttribute> AttributeCache = new ConcurrentDictionary<Type, ProtocolDataAttribute>();

        [IgnoreProperty]
        public EProtocolId ProtocolId { get; }
        [IgnoreProperty]
        public bool IsEncrypt { get; }
        
        protected BaseProtocolData()
        {
            var type = GetType(); 
            if (!AttributeCache.TryGetValue(type, out var cached))
            {
                var attribute = type.GetCustomAttribute<ProtocolDataAttribute>();
                cached = attribute ?? throw new InvalidCastException($"Invalid protocol type: {type}");
                AttributeCache[type] = cached;
            }
            
            ProtocolId = cached.ProtocolId;
            IsEncrypt = cached.IsEncrypt;
        }
    }

}
