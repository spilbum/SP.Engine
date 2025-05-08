using SP.Engine.Runtime.Security;
using SP.Engine.Server;
using SP.Engine.Server.Connector;

namespace SP.GameServer;

public class GameServer : Engine<UserPeer>
{
    public override UserPeer CreatePeer(ISession<UserPeer> session, DhKeySize dhKeySize, byte[] dhPublicKey)
    {
        return new UserPeer(EPeerType.User, session, dhKeySize, dhPublicKey);
    }

    protected override IServerConnector CreateConnector(string name)
    {
        throw new NotImplementedException();
    }
}
