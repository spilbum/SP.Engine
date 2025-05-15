using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolDataAttribute : Attribute
    {
        public EProtocolId ProtocolId { get; }
        public bool IsEncrypt { get; }

        public ProtocolDataAttribute(EProtocolId protocolId, bool isEncrypt = false)
        {
            ProtocolId = protocolId;
            IsEncrypt = isEncrypt;
        }
    }
}
