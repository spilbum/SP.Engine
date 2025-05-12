using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Handler
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolHandlerAttribute : Attribute
    {
        public EProtocolId ProtocolId { get; }
        
        public ProtocolHandlerAttribute(EProtocolId protocolId)
        {
            ProtocolId = protocolId;
        }
    }
}







