using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Command
{
    public abstract class CommandBase<TContext, TProtocol> : ICommand
        where TContext : ICommandContext
        where TProtocol : class, IProtocolData, new()
    {
        public string Name => GetType().Name;
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

            var instance = ProtocolPool<TProtocol>.Rent();

            try
            {
                if (!message.Deserialize(instance, ctx.Encryptor, ctx.Compressor))
                {
                    throw new InvalidOperationException($"Failed to deserialize message: {message.Id}");    
                }
                
                ExecuteCommand(ctx, instance);
            }
            catch (Exception e)
            {
                context.Logger.Error(e, "Command execution failed in {0}", Name);
            }
            finally
            {
                ProtocolPool<TProtocol>.Return(instance);
            }
        }

        protected abstract void ExecuteCommand(TContext context, TProtocol protocol);
    }
}
