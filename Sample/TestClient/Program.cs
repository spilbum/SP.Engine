using System.Text;
using Common;
using SP.Common;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;

namespace TestClient
{
    public class NetPeerManager
    {
        private static NetPeerManager? _instance;
        public static NetPeerManager Instance => _instance ??= new NetPeerManager();
        
        private NetPeer? _netPeer;
        private ILogger? _logger;
        private readonly Dictionary<EProtocolId, ProtocolMethodInvoker> _invokerDict = new();
        
        public bool Start(string ip, int port, ILogger? logger = null)
        {
            if (_netPeer?.State is ENetPeerState.Connecting or ENetPeerState.Open)
                throw new InvalidOperationException("Already connecting...");
            
            _logger = logger;
            _netPeer = new NetPeer(logger);
            _netPeer.ReconnectionIntervalSec = 3;
            _netPeer.AutoSendPingIntervalSec = 1;
            _netPeer.Connected += OnConnected;
            _netPeer.Disconnected += OnDisconnected;
            _netPeer.Error += OnError;
            _netPeer.MessageReceived += OnMessageReceived;
            _netPeer.Offline += OnOffline;
            _netPeer.StateChanged += OnStateChanged;
            if (!ProtocolMethodInvoker.LoadInvokers(GetType()).All(invoker => _invokerDict.TryAdd(invoker.ProtocolId, invoker)))
                return false;
            
            _netPeer.Open(ip, port);
            return true;
        }

        public void Stop()
        {
            _netPeer?.Close();
        }

        public void Update()
        {
            _netPeer?.Update();
        }

        public void Send(IProtocolData protocol)
        {
            _netPeer?.Send(protocol);
        }
        
        private void OnStateChanged(ENetPeerState obj)
        {
        }

        private void OnOffline(object? sender, EventArgs e)
        {
        }

        private void OnMessageReceived(object? sender, MessageEventArgs e)
        {
            var message = e.Message;
            if (_invokerDict.TryGetValue(message.ProtocolId, out var invoker))
                invoker.Invoke(this, message, _netPeer?.DhSharedKey);
        }

        private void OnError(object? sender, ErrorEventArgs e)
        {
            _logger?.Error(e.GetException());
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
        }

        private void OnConnected(object? sender, EventArgs e)
        {
        }

        [ProtocolMethod(Protocol.S2C.Echo)]
        private void OnEchoMessage(ProtocolData.S2C.Echo protocol)
        {
            _logger?.Info("Message received: {0}, bytes={1}, time={2}", protocol.Str, protocol.Bytes?.Length, protocol.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        }
    }
    
    internal static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length != 2)
                throw new Exception("Invalid number of arguments");
            
            var ip = args[0];
            var port = int.Parse(args[1]);

            var logger = new ConsoleLogger("TestClient");
            if (!NetPeerManager.Instance.Start(ip, port, logger))
                throw new Exception("Failed to initialize");
            
            while (true)
            {
                NetPeerManager.Instance.Update();

                if (Console.KeyAvailable)
                {
                    var str = Console.ReadLine();
                    var splits = str?.Split(' ');
                    if (splits == null)
                        continue;
                    
                    switch (splits[0])
                    {
                        case "echo":
                            NetPeerManager.Instance.Send(new ProtocolData.C2S.Echo { Str = splits[1], Bytes = Encoding.UTF8.GetBytes(splits[1]), Time = DateTime.Now});
                            break;
                        case "exit":
                            NetPeerManager.Instance.Stop();
                            break;
                        default:
                            logger.Error("Unknown command: {0}", str);
                            break;
                    }
                    
                    
                }
                
                Thread.Sleep(50);
            }
        }

    
    }
}

