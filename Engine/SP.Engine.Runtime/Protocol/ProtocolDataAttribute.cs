using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolDataAttribute : Attribute
    {
        public EProtocolId ProtocolId { get; }
        public EProtocolType ProtocolType { get; }

        public ProtocolDataAttribute(EProtocolId protocolId, EProtocolType protocolType = EProtocolType.Tcp)
        {
            ProtocolId = protocolId;
            ProtocolType = protocolType;
        }
    }
}
