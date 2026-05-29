using System;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Command
{
    public interface ICommand
    {
        string Name { get; }
        Type ContextType { get; }
        void Execute(ICommandContext context, IMessage message);
    }
}
