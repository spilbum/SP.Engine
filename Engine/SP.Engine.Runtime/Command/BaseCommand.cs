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
            if (!(context is TContext ctx))
                return;

            using (message)
            {
                var protocol = context.Deserialize<TProtocol>(message);
                ExecuteCommand(ctx, protocol);
            }
        }

        protected abstract void ExecuteCommand(TContext context, TProtocol protocol);
    }
}
