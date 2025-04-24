using System;

namespace SP.Engine.Core.Protocols
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
