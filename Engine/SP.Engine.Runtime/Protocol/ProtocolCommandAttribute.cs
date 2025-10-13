using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolCommandAttribute : Attribute
    {
        public ushort Id { get; }
        
        public ProtocolCommandAttribute(ushort id)
        {
            Id = id;
        }
    }
}







