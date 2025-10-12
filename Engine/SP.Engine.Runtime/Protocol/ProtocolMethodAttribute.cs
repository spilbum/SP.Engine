using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ProtocolMethodAttribute : Attribute
    {
        public ProtocolMethodAttribute(ushort id)
        {
            Id = id;
        }

        public ushort Id { get; }
    }
}


