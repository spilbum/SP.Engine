using System;
using System.IO;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public abstract class BaseCommand<TContext, TProtocol> : ICommand
        where TContext : ICommandContext
        where TProtocol : IProtocolData
    {
        public Type ContextType => typeof(TContext);

        public void Execute(ICommandContext context, IProtocolData protocol)
        {
            if (!(context is TContext ctx)) return;
            if (!(protocol is TProtocol p)) return;

            try
            {
                ExecuteCommand(ctx, p);
            }
            catch (Exception e)
            {
                context.Logger.Error(e);
            }
        }
        
        public void Execute(ICommandContext context, IMessage message)
        {
            if (!(context is TContext ctx)) return;

            try
            {
                var p = (TProtocol)Deserialize(context, message);
                ExecuteCommand(ctx, p);
            }
            catch (Exception e)
            {
                context.Logger.Error(e);
            }
        }

        public IProtocolData Deserialize(ICommandContext context, IMessage message)
        {
            try
            {
                var p = context.Deserialize<TProtocol>(message);
                if (p == null)
                    throw new InvalidDataException($"Failed to deserialize message: id={message.Id}");
                return p;
            }
            catch (Exception e)
            {
                context.Logger.Error(e);
                return null;
            }
        }

        protected abstract void ExecuteCommand(TContext context, TProtocol protocol);
    }
}
