using SampleServer.Connector;
using SP.Engine.Runtime.Security;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;

namespace SampleServer;

public class SampleServer : Engine<UserPeer>
{
    public bool Setup(string[] args)
    {
        var config = new EngineConfig
        {
            Listeners = [new ListenerConfig { Ip = "127.0.0.1", Mode = ESocketMode.Tcp, Port = 10000 }],
            Connectors = [new ConnectorConfig { Host = "127.0.0.1", Name = "SharedServer", Port = 20000 }]
        };

        if (!base.Initialize("SampleServer", config))
            return false;

        if (!base.Start())
            return false;
        
        Logger.Info("Server '{0}' instance setupped", Name);
        return true;
    }
    
    public override UserPeer CreatePeer(ISession<UserPeer> session, DhKeySize dhKeySize, byte[] dhPublicKey)
    {
        return new UserPeer(EPeerType.User, session, dhKeySize, dhPublicKey);
    }

    protected override IServerConnector CreateConnector(string name)
    {
        return name switch
        {
            "SharedServer" => new SharedServerConnector(),
            _ => throw new ArgumentException($"Unknown connector: {name}")
        };
    }
}
