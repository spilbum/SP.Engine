using Common;
using SP.Core.Logging;
using SP.Engine.Client.Configuration;
using SP.Shared.Resource;
using SP.Shared.Resource.Refs;
using SP.Shared.Resource.Schs;
using SP.Shared.Resource.Table;

namespace GameClient;

internal static class Program
{
    private static NetworkClient? _client;
    private static ResourceManager? _resourceManager;
    private static int _resourceVersion = -1;
    
    private static async Task CheckResourceServer(
        StoreType storeType, 
        string buildVersion,
        CancellationToken ct)
    {
        _resourceManager = new ResourceManager("http://localhost:5001");
        var response = await _resourceManager.BootstrapAsync(
            storeType,
            buildVersion,
            _resourceVersion,
            "1234",
            ct);

        if (!response.IsAllow)
        {
            Console.WriteLine("Not allowed");
            var storeUrl = response.StoreUrl;
            if (!string.IsNullOrWhiteSpace(storeUrl))
            {
                Console.WriteLine(storeUrl);
            }
            return;
        }

        _resourceVersion = response.LatestResourceVersion;

        if (!string.IsNullOrEmpty(response.DownloadSchsFileUrl) && !string.IsNullOrEmpty(response.DownloadRefsFileUrl))
        {
            var outputPath = Path.Combine(Environment.CurrentDirectory, "patch");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);
            
            var schsPath = await DownloadFileAsync(
                response.DownloadSchsFileUrl,
                outputPath,
                ct);
            if (string.IsNullOrEmpty(schsPath))
                throw new InvalidOperationException("Invalid schs file url.");
            
            var refsPath = await DownloadFileAsync(
                response.DownloadRefsFileUrl, 
                outputPath,
                ct);
            
            if (string.IsNullOrEmpty(refsPath))
                throw new InvalidOperationException("Invalid refs file url.");

            var schemas = await SchsPackReader.ReadAsync(schsPath, ct);
            var tables = await RefsPackReader.ReadAsync(schemas, refsPath, ct);
            ReferenceTableManager.Instance.Initialize(schemas, tables);
        }

        var server = response.Server;
        if (server == null)
        {
            return;
        }
        
        ConnectTo(server);
    }

    private static async Task<string?> DownloadFileAsync(string url, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient();

            var path = null == outputPath 
                ? Path.GetFileName(url)
                : Path.Combine(outputPath, Path.GetFileName(url));
            
            await using var fs = File.Create(path);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed: {url}");
                Console.WriteLine($"Status: {response.StatusCode}");
                return null;
            }
            
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await stream.CopyToAsync(fs, cancellationToken);
            
            return path;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return null;
        }
    }

    private static void ConnectTo(ServerConnectInfo server)
    {
        var config = EngineConfigBuilder.Create()
            .WithAutoPing(true, 2)
            .WithConnectAttempt(2, 15)
            .WithReconnectAttempt(5, 30)
            .WithUdpMtu(1200)
            .WithKeepAlive(true, 30, 2)
            .WithUdpKeepAlive(true, 30)
            .WithLatencySampleWindowSize(20)
            .Build();

        var logger = new ConsoleLogger("GameClient");
        _client = new NetworkClient(config, logger);
        _client.Connect(server.Host, server.Port);
    }
    
    private static async Task Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _client?.Close();
            Environment.Exit(0);
        };
        
        try
        {
            if (args.Length != 2)
                throw new InvalidOperationException("Invalid number of arguments.");
            
            if (!Enum.TryParse(args[0], out StoreType storeType))
                throw new ArgumentException($"Invalid store type: {args[1]}");
            
            var buildVersion = args[1];
            
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await CheckResourceServer(storeType, buildVersion, cts.Token);

            while (true)
            {
                _client?.Tick();

                if (Console.KeyAvailable)
                {
                    var line = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    line = line.Trim();
                    HandleCommand(line);
                }

                Thread.Sleep(50);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
    
    private static void HandleCommand(string line)
    {
        var splits = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = splits[0].ToLowerInvariant();
        var args = splits.Skip(1).ToArray();

        if (_client == null)
        {
            Console.WriteLine("client is null");
            return;
        }
        
        switch (command)
        {
            case "login":
                switch (args.Length)
                {
                    case 0:
                        _client.LoginReq(_client.Uid, _client.AccessToken, _client.Name, _client.CountryCode);
                        break;
                    case 2:
                    {
                        var uid = Convert.ToInt64(args[0]);
                        var accessToken = args[1];
                        _client.LoginReq(uid, accessToken, "none", "--");
                        break;
                    }
                    case 4:
                    {
                        var uid = Convert.ToInt64(args[0]);
                        var accessToken = args[1];
                        var name = args[2];
                        var countryCode = args[3];
                        _client.LoginReq(uid, accessToken, name, countryCode);
                        break;
                    }
                    default:
                        Console.WriteLine("usage: login [Uid] [AccessToken] [Name] [CountryCode]");
                        break;
                }

                break;
            case "create":
            {
                if (args.Length != 5)
                {
                    Console.WriteLine(
                        "usage: create [Kind] [Visibility] [MaxMembers] [ReadyCountdownSec] [MatchDurationSec]");
                    break;
                }

                var kind = (RoomKind)Convert.ToByte(args[0]);
                var visibility = (RoomVisibility)Convert.ToByte(args[1]);
                var maxMembers = Convert.ToInt32(args[2]);
                var readyCountdownSec = Convert.ToInt32(args[3]);
                var matchDurationSec = Convert.ToInt32(args[4]);
                _client.RoomCreateReq(kind, visibility, maxMembers, readyCountdownSec, matchDurationSec);
                break;
            }
            case "search":
            {
                if (args.Length != 1)
                {
                    Console.WriteLine("usage: search [RoomId]");
                    break;
                }

                var roomId = Convert.ToInt64(args[0]);
                _client.RoomSearchReq(roomId);
                break;
            }
            case "random":
            {
                if (args.Length != 5)
                {
                    Console.WriteLine(
                        "usage: random [Kind] [Visibility] [MaxMembers] [ReadyCountdownSec] [MatchDurationSec]");
                    break;
                }

                var kind = (RoomKind)Convert.ToByte(args[0]);
                var visibility = (RoomVisibility)Convert.ToByte(args[1]);
                var maxMembers = Convert.ToInt32(args[2]);
                var readyCountdownSec = Convert.ToInt32(args[3]);
                var matchDurationSec = Convert.ToInt32(args[4]);
                _client.RoomRandomSearchReq(kind, visibility, maxMembers, readyCountdownSec, matchDurationSec);
                break;
            }
            case "join":
            {
                if (args.Length != 1)
                {
                    Console.WriteLine("usage: join [RoomId]");
                    break;
                }

                var roomId = Convert.ToInt64(args[0]);
                _client.RoomJoinReq(roomId);
                break;
            }
            case "leave":
            {
                if (args.Length != 1)
                {
                    Console.WriteLine("usage: leave [Reason]");
                    break;
                }

                var reason = (RoomLeaveReason)Convert.ToByte(args[0]);
                _client.RoomLeaveNtf(reason);
                break;
            }
            case "action":
            {
                if (args.Length != 2)
                {
                    Console.WriteLine("usage: action [Action] [Value]");
                    break;
                }

                var action = (ActionKind)Convert.ToByte(args[0]);
                var value = args[1];
                _client.GameActionReq(action, value);
                break;
            }
            case "rank":
            {
                if (args.Length != 1)
                {
                    Console.WriteLine("usage: rank [Kind]");
                    break;
                }

                var kind = (SeasonKind)Convert.ToByte(args[0]);
                _client.RankMyReq(kind);
                break;
            }
            case "top":
            {
                if (args.Length != 2)
                {
                    Console.WriteLine("usage: top [Kind] [Count]");
                    break;
                }

                var kind = (SeasonKind)Convert.ToByte(args[0]);
                var count = Convert.ToInt32(args[1]);
                _client.RankTopReq(kind, count);
                break;
            }
            case "range":
            {
                if (args.Length != 3)
                {
                    Console.WriteLine("usage: range [Kind] [Start] [Count]");
                    break;
                }

                var kind = (SeasonKind)Convert.ToByte(args[0]);
                var start = Convert.ToInt32(args[1]);
                var count = Convert.ToInt32(args[2]);
                _client.RankRangeReq(kind, start, count);
                break;
            }
            case "quit":
            case "exit":
                _client.Close();
                Environment.Exit(0);
                break;
        }
    }
}
