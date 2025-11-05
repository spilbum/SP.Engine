using Common;
using SP.Core.Logging;
using SP.Engine.Client.Configuration;

namespace GameClient;

internal static class Program
{
    private static GameClient _client = null!;

    private static void Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _client.Close();
            Environment.Exit(0);
        };

        var host = string.Empty;
        var port = 0;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--host": host = args[++i]; break;
                case "--port": port = int.Parse(args[++i]); break;
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

        var logger = new ConsoleLogger("GameClient");

        try
        {
            _client = new GameClient(config, logger);
            _client.Connect(host, port);

            while (true)
            {
                _client.Tick();

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
            logger.Error(e);
        }
    }

    private static void HandleCommand(string line)
    {
        var splits = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = splits[0].ToLowerInvariant();
        var args = splits.Skip(1).ToArray();

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
