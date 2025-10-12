using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client.ProtocolHandler
{
    public interface IHandler
    {
        
    }
    
    public interface IHandler<in TSession, in TMessage> : IHandler
        where TMessage : IMessage
    {
        void ExecuteMessage(TSession session, TMessage message);
    }
}
