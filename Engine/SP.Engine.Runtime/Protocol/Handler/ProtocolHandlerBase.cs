using System;
using System.Diagnostics;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Protocol
{
    public abstract class ProtocolHandlerBase<TContext, TProtocol> : IProtocolHandler
        where TContext : IProtocolHandlerContext
        where TProtocol : class, IProtocolData, new()
    {
        public string Name { get; }

        protected ProtocolHandlerBase()
        {
            Name = GetType().Name;
        }

        public long Execute(IProtocolHandlerContext context, IMessage message)
        {
            if (!(context is TContext ctx)) return 0;

            using var scope = ProtocolScope<TProtocol>.Rent();
            var protocol = scope.Protocol;

            try
            {
                message.Deserialize(protocol, ctx.Encryptor, ctx.Compressor);
            }
            catch (Exception ex)
            {
                context.Logger.Error(ex, "ProtocolHandler '{0}' deserialize failed.", Name);
                return 0;
            }

            var start = Stopwatch.GetTimestamp();

            try
            {
                ExecuteProtocol(ctx, protocol);
            }
            catch (Exception e)
            {
                context.Logger.Error(e, "ProtocolHandler '{0}' execution failed.", Name);
            }

            var deltaTicks = Stopwatch.GetTimestamp() - start;
            return deltaTicks * 1000 / Stopwatch.Frequency;
        }

        protected abstract void ExecuteProtocol(TContext context, TProtocol protocol);
    }
}
