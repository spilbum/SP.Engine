using System;
using System.Threading.Tasks;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public abstract class BaseCommand<TContext, TProtocol> : ICommand
        where TContext : ICommandContext
        where TProtocol : IProtocolData
    {
        public Type ContextType => typeof(TContext);

        public Task Execute(ICommandContext context, IMessage message)
        {
            if (!(context is TContext ctx))
                return Task.CompletedTask;

            var protocol = context.Deserialize<TProtocol>(message);
            return ExecuteCommand(ctx, protocol);
        }

        protected abstract Task ExecuteCommand(TContext context, TProtocol protocol);
    }
}
