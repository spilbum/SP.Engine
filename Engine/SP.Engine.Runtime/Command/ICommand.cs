using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public interface ICommand
    {
        string Name { get; }
        Type ContextType { get; }
        void Execute(ICommandContext context, IMessage message);
    }
}
