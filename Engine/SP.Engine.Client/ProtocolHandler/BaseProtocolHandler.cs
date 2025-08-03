using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    public abstract class BaseProtocolHandler<TProtocol> : IHandler<NetPeer, IMessage>
        where TProtocol : class, IProtocolData, new()
    {
        public void ExecuteMessage(NetPeer session, IMessage message)
        {
            var protocol = (TProtocol)message.Unpack(typeof(TProtocol), session.DiffieHellman.SharedKey);
            ExecuteProtocol(session, protocol);
        }

        protected abstract void ExecuteProtocol(NetPeer session, TProtocol protocol);
    }
}
