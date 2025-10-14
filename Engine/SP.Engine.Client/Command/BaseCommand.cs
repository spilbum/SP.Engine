using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    public abstract class BaseCommand<TPeer, TProtocol> : ICommand
        where TPeer : BaseNetPeer
        where TProtocol : class, IProtocolData, new()
    {
        public void Execute(ICommandContext context, IMessage message)
        {
            try
            {
                if (!(context is TPeer peer)) throw new InvalidCastException($"Context must be {typeof(TPeer).Name}");
                var protocol = context.Deserialize<TProtocol>(message);
                ExecuteProtocol(peer, protocol);
            }
            catch (Exception e)
            {
                context.Logger.Error(e);
            }
        }
        
        protected abstract void ExecuteProtocol(TPeer peer, TProtocol protocol);
    }
}
