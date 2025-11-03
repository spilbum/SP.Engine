using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public abstract class BaseCommand<TContext, TProtocol> : ICommand
        where TContext : ICommandContext
        where TProtocol : IProtocolData
    {
        public Type ContextType => typeof(TContext);

        public void Execute(ICommandContext context, IMessage message)
        {
            try
            {
                if (!(context is TContext typed))
                    throw new InvalidCastException($"Context must be {typeof(TContext).Name}");

                var protocol = context.Deserialize<TProtocol>(message);
                ExecuteProtocol(typed, protocol);
            }
            catch (Exception e)
            {
                context.Logger.Error(e);
            }
        }

        protected abstract void ExecuteProtocol(TContext context, TProtocol protocol);
    }
}
