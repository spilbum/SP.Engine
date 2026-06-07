using System;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Command
{
    public interface ICommand
    {
        string Name { get; }
        Type ContextType { get; }
        double Execute(ICommandContext context, IMessage message);
    }
}
