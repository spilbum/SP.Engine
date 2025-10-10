using System;

namespace SP.Engine.Runtime.Handler
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







