using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client.ProtocolHandler
{
    public interface IHandler<in TSession, in TMessage>
        where TMessage : IMessage
    {
        void ExecuteMessage(TSession session, TMessage message);
    }
}
