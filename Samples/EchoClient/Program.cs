using Common;
using SP.Common.Logging;
using SP.Engine.Client.Configuration;

namespace EchoClient
{
    internal static class Program
    {
        private static EchoClient? _client;
        public static void Main(string[] args)
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _client?.Close();
                Environment.Exit(0);
            };
                
            var host = string.Empty;
            var port = 0;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--host": host = args[++i]; break;
                    case "--port": port = int.Parse(args[++i]); break;
                }
            }

            var config = EngineConfigBuilder.Create()
                .WithAutoPing(true, 2)
                .WithConnectAttempt(2, 15)
                .WithReconnectAttempt(5, 30)
                .WithUdpMtu(1200)
                .WithKeepAlive(true, 30, 2)
                .WithUdpKeepAlive(true, 30)
                .WithLatencySampleWindowSize(20)
                .Build();

            var logger = new ConsoleLogger("EchoClient");
            _client = new EchoClient(logger, config);
            _client.Open(host, port);

            while (true)
            {
                _client.Tick();

                if (Console.KeyAvailable)
                {
                    var line = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    HandleCommand(line.Trim());
                }
                
                Thread.Sleep(50);
            }
            

        }

        private static void HandleCommand(string line)
        {
            var sp = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = sp[0].ToLowerInvariant();

            switch (cmd)
            {
                case "help":
                {
                    Console.WriteLine(@"
commands:
    help            - show this
    ping            - send ping once (manual)
    tcp <bytes>     - send TCP echo payload with given size
    udp <bytes>     - send UDP echo payload with given size
    spam <proto> <bytes> <intervalMs> <seconds>
                    - spam tcp|udp echo for given duration
    stats           - print traffic/latency
    quit / exit     - close and exit
");
                    break;
                }
                case "ping":
                    _client?.SendPing();
                    _client?.Logger.Info("[Ping] sent");
                    break;
                
                case "tcp":
                    if (sp.Length < 2) 
                    { 
                        Console.WriteLine("usage: tcp <bytes>");
                        break;
                    }
                    SendEcho(sp[1], false);
                    break;
                
                case "udp":
                    if (sp.Length < 2) 
                    { 
                        Console.WriteLine("usage: udp <bytes>");
                        break;
                    }
                    SendEcho(sp[1], true);
                    break;
                
                case "spam":
                    if (sp.Length < 5) { Console.WriteLine("usage: spam <tcp|udp> <bytes> <intervalMs> <seconds>"); break; }
                    SpamEcho(sp[1].Equals("udp", StringComparison.OrdinalIgnoreCase), int.Parse(sp[2]), int.Parse(sp[3]), int.Parse(sp[4]));
                    break;
                
                case "stats":
                    PrintStats();
                    break;
                
                case "quit":
                case "exit":
                    _client?.Close();
                    Environment.Exit(0);
                    break;
            }
        }
        
        private static byte[] GenerateRandomBytes(int length)
        {
            var buffer = new byte[length];
            var rng = new Random();
            rng.NextBytes(buffer);
            return buffer;
        }

        public static long GetUnixTimestamp()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        private static void SendEcho(string bytesStr, bool isUdp)
        {
            var size = int.Parse(bytesStr);
            var payload = GenerateRandomBytes(size);
            var now = GetUnixTimestamp();

            if (isUdp)
            {
                var req = new C2SProtocolData.UdpEchoReq
                {
                    SendTime = now,
                    Data = payload
                };
                _client?.Send(req);
            }
            else
            {
                var req = new C2SProtocolData.TcpEchoReq
                {
                    SendTime = now,
                    Data = payload
                };
                _client?.Send(req);
            }
            
            _client?.Logger.Info($"[Echo-{(isUdp ? "UDP" : "TCP")}] send: {size} bytes");
        }

        private static void SpamEcho(bool isUdp, int size, int intervalMs, int seconds)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            var rand = new Random();
            
            new Thread(() =>
            {
                while (DateTime.UtcNow < end)
                {
                    var payload = new byte[size];
                    rand.NextBytes(payload);
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    if (isUdp)
                    {
                        var req = new C2SProtocolData.UdpEchoReq { SendTime = now, Data = payload };
                        _client?.Send(req);
                    }
                    else
                    {
                        var req = new C2SProtocolData.TcpEchoReq { SendTime = now, Data = payload };
                        _client?.Send(req);
                    }
                    
                    Thread.Sleep(intervalMs);
                }
                
                _client?.Logger.Info("[Spam] done.");
            })
            {
                IsBackground = true
            }.Start();
            
            _client?.Logger.Info($"[Spam] {(isUdp ? "UDP" : "TCP")} size={size}, interval={intervalMs}ms, duration={seconds}s");
        }

        private static void PrintStats()
        {
            if (_client == null)
                return;
            
            var (ts, tr) = _client.GetTcpTrafficInfo();
            var (us, ur) = _client.GetUdpTrafficInfo();
            var ls = _client.GetLatencyStats();

            _client.Logger.Info($"[Stats] TCP sent/recv={ts}/{tr} bytes | UDP sent/recv={us}/{ur} bytes | " +
                         $"RTT last={ls.LastRttMs:F1}ms avg={ls.AvgRttMs:F1}ms jitter={ls.JitterMs:F1}ms loss={ls.PacketLossRate:F2}%");
        }
    }
}

