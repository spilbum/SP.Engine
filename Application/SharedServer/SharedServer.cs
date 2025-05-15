using SharedServer.Peer;
using SP.Engine.Runtime.Security;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;

namespace SharedServer;

public class SharedServer : Engine<ServerPeer>
{
    private static SharedServer? _instance;

    public static SharedServer Instance
    {
        get => _instance!;
        private set => _instance = value;
    }

    public SharedServer()
    {
        Instance = this;
    }

    public bool Setup(string[] args)
    {
        var config = new EngineConfig
        {
            Listeners = [new ListenerConfig { Ip = "127.0.0.1", Port = 20000 }]
        };

        if (!base.Initialize("SharedServer", config))
            return false;

        if (!base.Start())
            return false;

        Logger.Info("Server '{0}' instance setupped", Name);
        return true;
    }
    
    public bool AddPeer(ServerPeer peer)
    {
        if (!AddOrUpdatePeer(peer)) return false;
        Logger.Info("Peer added: {0}({1})", peer.PeerId);
        return true;
    }
    
    public override ServerPeer CreatePeer(ISession<ServerPeer> session, DhKeySize dhKeySize, byte[] dhPublicKey)
    {
        return new ServerPeer(session, dhKeySize, dhPublicKey);
    }

    protected override IServerConnector? CreateConnector(string name)
    {
        return name switch
        {
            _ => (IServerConnector?)null
        };
    }
}
