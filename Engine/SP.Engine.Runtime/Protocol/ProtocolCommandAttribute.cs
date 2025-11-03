using System;

namespace SP.Engine.Runtime.Protocol
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolCommandAttribute : Attribute
    {
        public ProtocolCommandAttribute(ushort id)
        {
            Id = id;
        }

        public ushort Id { get; }
    }
}
