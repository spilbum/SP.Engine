using System;

namespace SP.Engine.Runtime.Command
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CommandHandlerAttribute : Attribute
    {
        public ushort Id { get; }
        public CommandHandlerAttribute(ushort id) => Id = id;
    }
}
