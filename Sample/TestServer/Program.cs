﻿using SP.Engine.Runtime;
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

            var config = new EngineConfig();
            config.Listeners.Add(new ListenerConfig { Ip = ip, Port = port, Mode = ESocketMode.Tcp });
            config.Listeners.Add(new ListenerConfig { Ip = ip, Port = 8080, Mode = ESocketMode.Udp });
            config.UseEncryption = true;
            config.UseCompression = true;
            config.CompressionThresholdPercent = 5;
            
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

public class NetPeer(IClientSession clientSession, DhKeySize dhKeySize, byte[] dhPublicKey)
    : BasePeer(EPeerType.User, clientSession, dhKeySize, dhPublicKey)
{
    
}

public class TestServer : Engine<NetPeer>
{
    public override NetPeer CreatePeer(IClientSession<NetPeer> iClientSession, DhKeySize dhKeySize, byte[] dhPublicKey)
    {
        return new NetPeer(iClientSession, dhKeySize, dhPublicKey);
    }

    protected override IServerConnector CreateConnector(string name)
    {
        throw new NotImplementedException();
    }
}
