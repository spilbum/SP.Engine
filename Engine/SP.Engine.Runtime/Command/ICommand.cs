using System;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Command
{
    public interface ICommand
    {
        Type ContextType { get; }
        void Execute(ICommandContext context, IMessage message);
    }
}
