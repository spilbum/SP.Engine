using SP.Engine.Runtime.Security;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;

namespace TestServer;

internal static class Program
{
    private static void Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
                throw new ApplicationException("Expected 2 arguments");
            
            var ip = args[0];
            var port = int.Parse(args[1]);
            
            var config = new EngineConfig
            {
                Listeners = [new ListenerConfig { Ip = ip, Port = port, Mode = ESocketMode.Tcp }]
            };
            
            using var server = new TestServer();
            if (!server.Initialize("TestServer", config))
                throw new Exception("Failed to initialize server");
        
            if (!server.Start())
                throw new Exception("Failed to start server");

            while (true)
            {
                Thread.Sleep(50);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}

public class NetPeer(ISession session, DhKeySize dhKeySize, byte[] dhPublicKey)
    : BasePeer(EPeerType.User, session, dhKeySize, dhPublicKey);

public class TestServer : Engine<NetPeer>
{
    public override NetPeer CreatePeer(ISession<NetPeer> session, DhKeySize dhKeySize, byte[] dhPublicKey)
    {
        return new NetPeer(session, dhKeySize, dhPublicKey);
    }

    protected override IServerConnector CreateConnector(string name)
    {
        throw new NotImplementedException();
    }

    public bool Setup(string[] args)
    {
        var ip = args[0];
        var port = int.Parse(args[1]);

        var config = new EngineConfig
        {
            Listeners = [new ListenerConfig { Ip = ip, Port = port, Mode = ESocketMode.Tcp }]
        };
        
        if (!base.Initialize("TestServer", config))
            return false;
        
        if (!base.Start())
            return false;

        Logger.Info("Server started");
        return true;
    }
}
