using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace SP.Engine.Core.Protocol
{
    public interface IProtocolData
    {
        EProtocolId ProtocolId { get; }
        bool IsEncrypt { get; }
        uint CompressibleSize { get; }
    }

    public abstract class BaseProtocolData : IProtocolData
    {
        private static readonly ConcurrentDictionary<Type, ProtocolAttribute> AttributeCache = new ConcurrentDictionary<Type, ProtocolAttribute>();

        public EProtocolId ProtocolId { get; set; }
        public bool IsEncrypt { get; set; }
        public uint CompressibleSize { get; set; }
        
        protected BaseProtocolData()
        {
            var type = GetType(); 
            if (!AttributeCache.TryGetValue(type, out var cached))
            {
                var attribute = type.GetCustomAttribute<ProtocolAttribute>();
                cached = attribute ?? throw new Exception($"Invalid protocol type: {type}");
                AttributeCache[type] = cached;
            }
            
            ProtocolId = cached.ProtocolId;
            IsEncrypt = cached.IsEncrypt;
            CompressibleSize = cached.CompressibleSize;
        }
    }

}
