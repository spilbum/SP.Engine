using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server.ProtocolHandler;

internal abstract class BaseEngineHandler<TSession, TProtocol> : BaseHandler<TSession, IMessage>
    where TSession : IClientSession
    where TProtocol : IProtocol
{
    public override void ExecuteMessage(TSession session, IMessage message)
    {
        try
        {
            var protocol = (TProtocol)message.Deserialize(typeof(TProtocol), session.Encryptor);
            if (protocol == null)
                throw new NullReferenceException($"Protocol could not be deserialized: {message.Id}");
            ExecuteProtocol(session, protocol);
        }
        catch (Exception e)
        {
            session.Logger.Error(e);
        }
    }
    
    protected abstract void ExecuteProtocol(TSession session, TProtocol data);
}
