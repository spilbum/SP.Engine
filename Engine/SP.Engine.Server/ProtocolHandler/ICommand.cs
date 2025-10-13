using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server.ProtocolHandler;

public interface ICommand
{
    void Execute(ICommandContext context, IMessage message);
}

