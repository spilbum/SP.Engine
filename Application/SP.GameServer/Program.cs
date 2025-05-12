
using System.Net;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.GameServer;

var config = new EngineConfig
{
    Listeners = [new ListenerConfig { Ip = "192.168.0.109", Mode = ESocketMode.Tcp, Port = 3542 }]
};

using var server = new GameServer();
if (!server.Initialize("GameServer", config))
{
    Console.WriteLine("Failed to initialize server");
    return;
}

if (!server.Start())
{
    Console.WriteLine("Failed to start server");
    return;
}

while (true)
{
    Thread.Sleep(100);
}
