using System;
using System.Reflection;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

public abstract class BaseHandler<TContext, TMessage> : IHandler<TContext, TMessage>
    where TContext : IHandleContext
    where TMessage : IMessage
{
    protected BaseHandler()
    {
        var attr = GetType().GetCustomAttribute<ProtocolHandlerAttribute>();
        if (attr == null)
            throw new InvalidOperationException($"{GetType().Name} must be decorated with ProtocolHandlerAttribute.");
        
        Id = attr.ProtocolId;
    }
    
    public EProtocolId Id { get; }

    public abstract void ExecuteMessage(TContext context, TMessage message);
}

