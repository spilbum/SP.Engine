using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

public abstract class BaseProtocolHandler<TPeer, TProtocol> : BaseHandler<TPeer, IMessage>
    where TPeer : BasePeer, IPeer
    where TProtocol : IProtocol
{
    public override void ExecuteMessage(TPeer peer, IMessage message)
    {
        try
        {
            var protocol = (TProtocol)message.Deserialize(typeof(TProtocol), peer.Encryptor, peer.Compressor);
            ExecuteProtocol(peer, protocol);
        }
        catch (Exception e)
        {
            peer.Logger.Error(e);
        }
    }
    
    protected abstract void ExecuteProtocol(TPeer peer, TProtocol protocol);
}
