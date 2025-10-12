using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolHandlerAttribute : Attribute
    {
        public ushort Id { get; }
        
        public ProtocolHandlerAttribute(ushort id)
        {
            Id = id;
        }
    }
}







