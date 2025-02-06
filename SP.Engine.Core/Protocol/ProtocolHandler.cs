using System;

namespace SP.Engine.Core.Protocol
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ProtocolHandlerAttribute : Attribute
    {
        public EProtocolId ProtocolId { get; private set; }
        
        public ProtocolHandlerAttribute(EProtocolId protocolId)
        {
            ProtocolId = protocolId;
        }
    }
}
