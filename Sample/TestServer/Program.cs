using SP.Engine.Runtime;
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
            var host = string.Empty;
            var port = 0;
            
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--host":
                        if (i + 1 < args.Length)
                            host = args[++i];
                        break;

                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedPort))
                            port = parsedPort;
                        i++;
                        break;
                }
            }
            
            if (host == string.Empty || port == 0)
                throw new ApplicationException("Invalid host or port argument");
            
            using var server = new TestServer();

            var config = EngineConfigBuilder.Create()
                .WithLimitConnectionCount(100)
                .WithSendTimeout(3000)
                .WithKeepAlive(false, 10, 2)
                .WithClearIdleSession(false, 60, 180)
                .WithEncryption(true)
                .WithCompression(true, 20)
                .AddListener(new ListenerConfig { Ip = host, Port = port, Mode = ESocketMode.Tcp })
                .AddListener(new ListenerConfig { Ip = host, Port = 10000, Mode = ESocketMode.Udp })
                .WithPeerUpdateInterval(50)
                .WithConnectorUpdateInterval(30)
                .Build();
            
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

public class NetPeer(IClientSession clientSession) : BasePeer(EPeerType.User, clientSession)
{
    
}

public class TestServer : Engine<NetPeer>
{
    protected override NetPeer CreatePeer(IClientSession<NetPeer> session)
    {
        return new NetPeer(session);
    }

    protected override IServerConnector CreateConnector(string name)
    {
        throw new NotImplementedException();
    }
}
