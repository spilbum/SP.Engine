using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

internal abstract class BaseEngineHandler<TSession, TProtocol> : BaseHandler<TSession, IMessage>
    where TSession : IClientSession
    where TProtocol : IProtocolData
{
    public override void ExecuteMessage(TSession session, IMessage message)
    {
        try
        {
            var protocol = (TProtocol)message.Unpack(typeof(TProtocol), null);
            if (protocol == null)
                throw new NullReferenceException($"Protocol could not be deserialized: {message.ProtocolId}");
            ExecuteProtocol(session, protocol);
        }
        catch (Exception e)
        {
            session.Logger.Error(e);
        }
    }
    
    protected abstract void ExecuteProtocol(TSession session, TProtocol protocol);
}
