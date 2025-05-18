using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

public interface IHandler
{
    EProtocolId Id { get; }
}

public interface IHandler<in TContext, in TMessage> : IHandler
    where TContext : IHandleContext
    where TMessage : IMessage
{
    void ExecuteMessage(TContext context, TMessage message);
}

