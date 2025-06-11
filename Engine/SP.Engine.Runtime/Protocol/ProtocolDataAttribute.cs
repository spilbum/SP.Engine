using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolDataAttribute : Attribute
    {
        public EProtocolId ProtocolId { get; }

        public ProtocolDataAttribute(EProtocolId protocolId)
        {
            ProtocolId = protocolId;
        }
    }
}
