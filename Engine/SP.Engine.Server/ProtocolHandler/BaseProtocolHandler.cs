using System;
using SP.Engine.Runtime.Message;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

public abstract class BaseProtocolHandler<TPeer, TProtocol> : BaseHandler<TPeer, IMessage>
    where TPeer : BasePeer, IPeer
    where TProtocol : IProtocolData
{
    public override void ExecuteMessage(TPeer peer, IMessage message)
    {
        try
        {
            var protocol = (TProtocol)message.DeserializeProtocol(typeof(TProtocol), peer.DhSharedKey);
            ExecuteProtocol(peer, protocol);
        }
        catch (Exception e)
        {
            peer.Logger.Error(e);
        }
    }
    
    protected abstract void ExecuteProtocol(TPeer peer, TProtocol protocol);
}
