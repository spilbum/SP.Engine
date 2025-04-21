using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace SP.Engine.Core.Protocol
{
    public abstract class BaseProtocolData : IProtocolData
    {
        private static readonly ConcurrentDictionary<Type, ProtocolAttribute> AttributeCache = new ConcurrentDictionary<Type, ProtocolAttribute>();

        public EProtocolId ProtocolId { get; }
        public bool IsEncrypt { get; }
        public uint CompressibleSize { get; }
        
        protected BaseProtocolData()
        {
            var type = GetType(); 
            if (!AttributeCache.TryGetValue(type, out var cached))
            {
                var attribute = type.GetCustomAttribute<ProtocolAttribute>();
                cached = attribute ?? throw new InvalidCastException($"Invalid protocol type: {type}");
                AttributeCache[type] = cached;
            }
            
            ProtocolId = cached.ProtocolId;
            IsEncrypt = cached.IsEncrypt;
            CompressibleSize = cached.CompressibleSize;
        }
    }

}
