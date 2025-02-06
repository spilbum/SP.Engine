using System;

namespace SP.Engine.Core.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolAttribute : Attribute
    {
        public EProtocolId ProtocolId { get; }
        public bool IsEncrypt { get; }
        public uint CompressibleSize { get; }
        
        public ProtocolAttribute(EProtocolId protocolId, bool isEncrypt = false, uint compressibleSize = 0)
        {
            ProtocolId = protocolId;
            IsEncrypt = isEncrypt;
            CompressibleSize = compressibleSize;
        }
    }
}
