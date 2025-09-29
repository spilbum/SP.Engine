using System;
using SP.Engine.Runtime.Protocol;

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







