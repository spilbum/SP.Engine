using System.Text;
using System.Timers;
using Common;
using SP.Common;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;
using Timer = System.Threading.Timer;

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
            _netPeer.ReconnectIntervalSec = 3;
            _netPeer.MaxReconnectAttempts = 3;
            _netPeer.IsEnableAutoSendPing = true;
            _netPeer.AutoSendPingIntervalSec = 2;
            _netPeer.MaxAllowedLength = 4096;
            _netPeer.UdpMtu = 1400;
            _netPeer.MaxConnectAttempts = 2;
            _netPeer.ConnectIntervalSec = 10;
            _netPeer.Connected += OnConnected;
            _netPeer.Disconnected += OnDisconnected;
            _netPeer.Error += OnError;
            _netPeer.MessageReceived += OnMessageReceived;
            _netPeer.Offline += OnOffline;
            _netPeer.StateChanged += OnStateChanged;
            if (!ProtocolMethodInvoker.LoadInvokers(GetType()).All(invoker => _invokerDict.TryAdd(invoker.ProtocolId, invoker)))
                return false;
            
            _netPeer.Connect(ip, port);
            return true;
        }

        public void Stop()
        {
            _netPeer?.Close();
        }

        public void Update()
        {
            _netPeer?.Tick();
        }

        public void Send(IProtocolData protocol)
        {
            _netPeer?.Send(protocol);
        }
        
        private void OnStateChanged(object? sender, StateChangedEventArgs e)
        {
            _logger?.Debug("[OnStateChanged] {0} -> {1}", e.OldState, e.NewState);
        }

        private void OnOffline(object? sender, EventArgs e)
        {
            _logger?.Debug("[OnOffline] Offline...");
        }

        private void OnMessageReceived(object? sender, MessageEventArgs e)
        {
            var message = e.Message;
            if (_invokerDict.TryGetValue(message.ProtocolId, out var invoker))
                invoker.Invoke(this, message, _netPeer?.DiffieHelman.SharedKey);
        }

        private void OnError(object? sender, ErrorEventArgs e)
        {
            _logger?.Error(e.GetException());
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            _logger?.Info("[OnDisconnected] Disconnected...");
        }

        private void OnConnected(object? sender, EventArgs e)
        {
            _logger?.Info("[OnConnected] Connected...");
        }

        public void PrintNetowrkQuality()
        {
            if (!_netPeer?.IsConnected ?? false)
                return;
            
            var sb = new StringBuilder();
            if (_netPeer != null)
            {
                sb.Append(
                    $"Avg: {_netPeer.AverageRttMs:F1} Min: {_netPeer.MinRttMs:F1} | Max: {_netPeer.MaxRttMs:F1} | Jitter: {_netPeer.JitterMs:F1} | PackLossRate: {_netPeer.PacketLossRate:F1}");
            }
            
            _logger?.Info("Network Quality: {0}, ServerTime: {1:yyyy-MM-dd hh:mm:ss.fff}", sb.ToString(), _netPeer?.GetServerTime());
        }

        [ProtocolMethod(Protocol.S2C.EchoAck)]
        private void OnEchoAck(ProtocolData.S2C.EchoAck protocol)
        {
            _logger?.Info("Message received: {0}, bytes={1}, sentTime={2}", protocol.Str, protocol.Bytes?.Length, protocol.SentTime);
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
                            var message = splits[1];
                            for (var i = 0; i < 5; i++) NetPeerManager.Instance.Send(new ProtocolData.C2S.EchoReq { Str = message, Bytes = Encoding.UTF8.GetBytes(message), SendTime = DateTime.UtcNow});
                            break;
                        case "fragment":
                        {
                            var bytes = GenerateRandomBytes(int.Parse(splits[1]));
                            NetPeerManager.Instance.Send(new ProtocolData.C2S.EchoReq { Str = "fragment", Bytes = bytes, SendTime = DateTime.UtcNow });
                            break;
                        }
                        case "test":
                        {
                            var cmd = splits[1];
                            switch (cmd)
                            {
                                case "start":
                                    _timer = new Timer(TestSend, null, TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(16));
                                    break;
                                case "stop":
                                    _timer?.Dispose();
                                    break;
                            }
                            
                            break;
                        }
                        case "exit":
                            NetPeerManager.Instance.Stop();
                            break;
                        default:
                            logger.Error("Unknown command: {0}", str);
                            break;
                    }
                    
                    
                }

                if (_nextPrintNetowrkQuality == null || DateTime.UtcNow > _nextPrintNetowrkQuality)
                {
                    _nextPrintNetowrkQuality = DateTime.UtcNow.AddSeconds(5);
                    NetPeerManager.Instance.PrintNetowrkQuality();
                }
                
                Thread.Sleep(50);
            }
        }

        private static DateTime? _nextPrintNetowrkQuality;
        private static readonly Random Random = new();

        private static byte[] GenerateRandomBytes(int length)
        {
            var buffer = new byte[length];
            Random.NextBytes(buffer);
            return buffer;
        }
        
        private static Timer? _timer;

        private static void TestSend(object? state)
        {
            NetPeerManager.Instance.Send(new ProtocolData.C2S.EchoReq
            {
                Str = "test",
                Bytes = "test"u8.ToArray(),
                SendTime = DateTime.UtcNow
            });
        }
    }
}

