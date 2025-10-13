using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client.Command
{
    public interface ICommand
    {   
        void Execute(ICommandContext context, IMessage message);
    }
}
