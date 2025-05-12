using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolDataAttribute : Attribute
    {
        public EProtocolId ProtocolId { get; }
        public bool IsEncrypt { get; }
        public uint CompressibleSize { get; }
        
        public ProtocolDataAttribute(EProtocolId protocolId, bool isEncrypt = false, uint compressibleSize = 0)
        {
            ProtocolId = protocolId;
            IsEncrypt = isEncrypt;
            CompressibleSize = compressibleSize;
        }
    }
}
