using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server.Command;

public interface ICommand
{
    void Execute(ICommandContext context, IMessage message);
}

