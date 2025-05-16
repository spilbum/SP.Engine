
using SP.Common.Logging;
using SP.Engine.Client;

namespace TestClient
{
    internal static class Program
    {
        private static NetPeer? _netPeer;
        private static ConsoleLogger? _logger;

        public static void Main(string[] args)
        {
            _logger = new ConsoleLogger("TestClient");
            _netPeer = new NetPeer(_logger);
            _netPeer.ReconnectionIntervalSec = 3;
            _netPeer.AutoSendPingIntervalSec = 1;
            _netPeer.Connected += OnConnected;
            _netPeer.Disconnected += OnDisconnected;
            _netPeer.Error += OnError;
            _netPeer.MessageReceived += OnMessageReceived;
            _netPeer.Offline += OnOffline;
            _netPeer.StateChanged += OnStateChanged;

            if (args.Length != 2)
                throw new Exception("Invalid number of arguments");
            
            var ip = args[0];
            var port = int.Parse(args[1]);
            
            _netPeer.Open(ip, port);
            while (true)
            {
                _netPeer.Update();

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                        break;
                }
                
                Thread.Sleep(50);
            }
        }

        private static void OnStateChanged(ENetPeerState obj)
        {
        }

        private static void OnOffline(object? sender, EventArgs e)
        {
        }

        private static void OnMessageReceived(object? sender, MessageEventArgs e)
        {
            _logger?.Info($"OnMessageReceived: {e.Message}");
        }

        private static void OnError(object? sender, ErrorEventArgs e)
        {
            _logger?.Error(e.GetException());
        }

        private static void OnDisconnected(object? sender, EventArgs e)
        {
        }

        private static void OnConnected(object? sender, EventArgs e)
        {
        }
    }
}

