using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public interface ICommand
    {
        Type ContextType { get; }
        void Execute(ICommandContext context, IProtocolData protocol);
        void Execute(ICommandContext context, IMessage message);
        IProtocolData Deserialize(ICommandContext context, IMessage message);
    }
}
