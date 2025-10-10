using SP.Engine.Server;
using SP.Engine.Server.Configuration;

namespace EchoServer;

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
            
            var config = EngineConfigBuilder.Create()
                .WithNetwork(n => n with
                {
                    UseCompress = true,
                    CompressionThreshold = 128
                })
                .WithSession(s => s with
                {
                })
                .WithRuntime(r => r with
                {
                })
                .AddListener(new ListenerConfig { Ip = host, Port = port, Mode = SocketMode.Tcp, BackLog = 128 })
                .AddListener(new ListenerConfig { Ip = host, Port = port + 1, Mode = SocketMode.Udp, BackLog = 128 })
                .Build();
            
            using var server = new EchoServer();
            
            if (!server.Initialize("Echo", config))
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


