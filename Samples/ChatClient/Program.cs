using SP.Core.Logging;

namespace ChatClient;

internal static class Program
{
    private static readonly CancellationTokenSource _cts = new();
    private static ChatManager? _manager;
    private static string? _host;
    private static int _port;
    private static string? _myGroup;
    private static string? _targetGroup;

    private static async Task Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: ChatClient.exe <Host> <Port> <MyGroup(A/B)> <TargetGroup(B/A)>");
            return;
        }

        _host = args[0];
        _port = int.Parse(args[1]);
        _myGroup = args[2].ToUpper();
        _targetGroup = args[3].ToUpper();

#if DEBUG
        const LogLevel minLevel = LogLevel.Debug;
#else
        const LogLevel minLevel = LogLevel.Info;
#endif

        var logger = new ConsoleLogger($"PeerPool-{_myGroup}", minLevel: minLevel);
        _manager = new ChatManager(logger);

        logger.Info("Bootstrap sequence loaded. Targeting Target Server => {0}:{1}", _host, _port);
        logger.Info("Available Context Commands: [connect, start, stop, disconnect, quit]");

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync(_cts.Token);
                await HandleCommandAsync(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Unhandled thread pool context exception occurred.");
        }
        finally
        {
            _manager.Stop();
            _cts.Dispose();
        }
    }

    private static async Task HandleCommandAsync(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = args[0].ToLower();

        if (_manager == null || string.IsNullOrWhiteSpace(_host) || string.IsNullOrWhiteSpace(_myGroup) ||
            string.IsNullOrWhiteSpace(_targetGroup)) return;

        switch (command)
        {
            case "connect":
            {
                if (args.Length != 3 || 
                    !int.TryParse(args[1], out var count) ||
                    !int.TryParse(args[2], out var delayMs))
                {
                    Console.WriteLine("Usage: connect <count> <delay(ms)>");
                    return;
                }

                if (_manager.IsRunning)
                {
                    await _manager.AddConnectAsync(count, _myGroup, _targetGroup, delayMs, _cts.Token);
                }
                else
                {
                    await _manager.StartConnectAsync(_host, _port, count, _myGroup, _targetGroup, delayMs, _cts.Token);
                }

                break;
            }

            case "start":
            {
                if (args.Length != 4
                    || !int.TryParse(args[1], out var targetCount)
                    || !int.TryParse(args[2], out var periodMs)
                    || !int.TryParse(args[3], out var batchCount))
                {
                    Console.WriteLine("Usage: start <targetCount> <period(ms)> <batchCount>");
                    return;
                }

                _manager.StartChatTest(targetCount, periodMs, batchCount);
                break;
            }

            case "stop":
            {
                _manager.StopChatTest();
                break;
            }

            case "disconnect":
            {
                _manager.Stop();
                break;
            }

            case "quit":
            {
                await _cts.CancelAsync();
                break;
            }
            default:
            {
                Console.WriteLine($"Command syntax error or unknown prefix: {command}");
                break;
            }
        }
    }
}
