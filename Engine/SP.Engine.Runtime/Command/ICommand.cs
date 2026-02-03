using System;
using System.Threading.Tasks;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Command
{
    public interface ICommand
    {
        Type ContextType { get; }
        Task Execute(ICommandContext context, IMessage message);
    }
}
