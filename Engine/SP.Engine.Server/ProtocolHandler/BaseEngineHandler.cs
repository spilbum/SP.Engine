using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

internal abstract class BaseEngineHandler<TSession, TProtocol> : BaseHandler<TSession, IMessage>
    where TSession : ISession
    where TProtocol : IProtocolData
{
    public override void ExecuteMessage(TSession session, IMessage message)
    {
        try
        {
            var protocol = (TProtocol)message.DeserializeProtocol(typeof(TProtocol));
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
