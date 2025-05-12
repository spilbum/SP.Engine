using SP.Engine.Runtime.Security;
using SP.Engine.Server;

namespace SP.GameServer;

public class UserPeer(EPeerType peerType, ISession session, DhKeySize dhKeySize, byte[] dhPublicKey)
    : BasePeer(peerType, session, dhKeySize, dhPublicKey);
