using System;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Command
{
    public interface ICommandHandler
    {
        string Name { get; }
        Type ContextType { get; }
        long Execute(ICommandContext context, IMessage message);
    }
}
