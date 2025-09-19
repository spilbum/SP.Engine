using System.Text;
using System.Timers;
using Common;
using SP.Common;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Client.Configuration;
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
        
        public bool Start(string ip, int port, EngineConfig config, ILogger? logger = null)
        {
            if (_netPeer?.State is ENetPeerState.Connecting or ENetPeerState.Open)
                throw new InvalidOperationException("Already connecting...");

            _logger = logger;
            _netPeer = new NetPeer(config, logger);
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
                invoker.Invoke(this, message, _netPeer?.Encryptor);
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
            if (_netPeer?.LatencyStats != null)
            {
                var stats = _netPeer.LatencyStats;
                sb.Append(
                    $"SRTT: {stats.SmoothedRttMs}Avg: {stats.AvgRttMs:F1} Min: {stats.MinRttMs:F1} | Max: {stats.MaxRttMs:F1} | Jitter: {stats.JitterMs:F1} | PackLossRate: {stats.PacketLossRate:F1}");
            }
            
            //_logger?.Info("Network Quality: {0}, ServerTime: {1:yyyy-MM-dd hh:mm:ss.fff}", sb.ToString(), _netPeer?.GetServerTime());
        }
        
        [ProtocolMethod(S2CProtocol.UdpEchoAck)]
        private void OnEchoAck(S2CProtocolData.UdpEchoAck data)
        {
            var rawRtt = Program.GetUnixTimestamp() - data.SentTime;
            _logger?.Debug($"[OnEchoAck] {rawRtt:F1} ms");
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

            var config = EngineConfigBuilder.Create()
                .WithAutoPing(false, 2)
                .WithConnectAttempt(2, 15)
                .WithReconnectAttempt(5, 30)
                .WithUdpMtu(1200)
                .WithUdpKeepAlive(false, 30)
                .Build();

            var logger = new ConsoleLogger("TestClient");
            
            if (!NetPeerManager.Instance.Start(ip, port, config, logger))
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
                        {
                            if (splits.Length != 4)
                            {
                                logger.Error("Invalid number of arguments");
                                continue;
                            }
                            
                            var sizeBytes = int.Parse(splits[1]);
                            var sendIntervalMs = int.Parse(splits[2]);
                            var durationTimeSec = int.Parse(splits[3]);
                            _endTime = DateTime.UtcNow.AddSeconds(durationTimeSec);
                            _timer = new Timer(_ => SendEcho(sizeBytes), null, 0, sendIntervalMs);
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

        private static DateTime _endTime;
        private static DateTime? _nextPrintNetowrkQuality;

        private static byte[] GenerateRandomBytes(int length)
        {
            var buffer = new byte[length];
            var rng = new Random();
            rng.NextBytes(buffer);
            return buffer;
        }

        public static long GetUnixTimestamp()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        private static Timer? _timer;

        private static void SendEcho(int sizeBytes)
        {
            if (_endTime < DateTime.UtcNow)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }
            
            var echoReq = new C2SProtocolData.UdpEchoReq { SendTime = GetUnixTimestamp(), Data = GenerateRandomBytes(sizeBytes) };
            NetPeerManager.Instance.Send(echoReq);
        }
    }
}

