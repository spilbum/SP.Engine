using NetworkCommon;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Server;

namespace SharedServer.Peer;

public class ServerPeer : BasePeer
{
    public string? ServerType { get; protected set; }
    
    protected ServerPeer(ServerPeer other)
        : base(other)
    {
        ServerType = other.ServerType;
    }

    public ServerPeer(ISession session, DhKeySize dhKeySize, byte[] dhPublicKey) : base(EPeerType.Server, session, dhKeySize, dhPublicKey)
    {
    }

    public virtual IProtocolData? ExecuteProtocol(IProtocolData protocol)
    {
        throw new NotImplementedException();
    }
}
