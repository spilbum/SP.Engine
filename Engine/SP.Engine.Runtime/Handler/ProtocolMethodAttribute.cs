using System;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Handler
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ProtocolMethodAttribute : Attribute
    {
        public ProtocolMethodAttribute(EProtocolId protocolId)
        {
            ProtocolId = protocolId;
        }

        public EProtocolId ProtocolId { get; }
    }
}


