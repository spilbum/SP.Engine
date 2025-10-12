using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.ProtocolHandler
{
    public abstract class BaseProtocolHandler<TProtocol> : IHandler<NetPeer, IMessage>
        where TProtocol : class, IProtocol, new()
    {
        public void ExecuteMessage(NetPeer peer, IMessage message)
        {
            var protocol = (TProtocol)message.Deserialize(typeof(TProtocol), peer.Encryptor, peer.Compressor);
            ExecuteProtocol(peer, protocol);
        }

        protected abstract void ExecuteProtocol(NetPeer peer, TProtocol protocol);
    }
}
