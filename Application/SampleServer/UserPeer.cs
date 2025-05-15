using SP.Engine.Runtime.Security;
using SP.Engine.Server;

namespace SampleServer;

public class UserPeer(EPeerType peerType, ISession session, DhKeySize dhKeySize, byte[] dhPublicKey)
    : BasePeer(peerType, session, dhKeySize, dhPublicKey);
