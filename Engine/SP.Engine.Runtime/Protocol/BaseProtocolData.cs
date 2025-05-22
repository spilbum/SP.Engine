using System;
using System.Collections.Generic;
using System.Reflection;
using SP.Common.Accessor;

namespace SP.Engine.Runtime.Protocol
{
    public abstract class BaseProtocolData : IProtocolData
    {
        private static readonly Dictionary<Type, EProtocolId> Cache = new Dictionary<Type, EProtocolId>();
        
        [IgnoreMember]
        public EProtocolId ProtocolId { get; }
        
        protected BaseProtocolData()
        {
            var type = GetType();
            if (Cache.TryGetValue(type, out var protocolId))
                ProtocolId = protocolId;
            else
            {
                var attr = type.GetCustomAttribute<ProtocolDataAttribute>()
                           ?? throw new InvalidCastException($"Invalid protocol data attribute: {type.FullName}");
                Cache[type] = attr.ProtocolId;
                ProtocolId = attr.ProtocolId;
            }
            
        }
    }

}
