using SP.Core.Logging;

namespace EchoClient;

internal static class Program
{
    private static readonly CancellationTokenSource _cts = new();
    private static EchoManager? _manager;
    private static string? _host;
    private static int _port;
    
    private static async Task Main(string[] args)
    {
        _host = args[0];
        _port = int.Parse(args[1]);
        
        #if DEBUG
        const LogLevel minLevel = LogLevel.Debug;
        #else
        const LogLevel minLevel = LogLevel.Info;
        #endif
        
        var logger = new ConsoleLogger("EchoClient", minLevel: minLevel);
        _manager = new EchoManager(logger);

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
            Console.WriteLine("An exception occurred: {0}", ex.Message);
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
        switch (command)
        {
            case "connect":
            {
                if (args.Length != 3 || !int.TryParse(args[1], out var count) || !int.TryParse(args[2], out var delayMs))
                {
                    Console.WriteLine("Usage: connect <count> <delay(ms)>");
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(_host) || _manager == null) return;

                if (_manager.IsRunning)
                {
                    await _manager.AddConnectAsync(count, delayMs, _cts.Token);
                }
                else
                {
                    await _manager.StartConnectAsync(_host, _port, count, delayMs, _cts.Token);
                }
                
                break;
            }

            case "start":
            {
                if (args.Length != 5 
                    || !int.TryParse(args[1], out var targetCount) 
                    || string.IsNullOrWhiteSpace(args[2])
                    || !int.TryParse(args[3], out var period) 
                    || !int.TryParse(args[4], out var batchCount))
                {
                    Console.WriteLine("Usage: start <targetCount> <sendType(tcp/udp)> <period(ms)> <batchCount>");
                    return;
                }

                var sendType = args[2];
                _manager?.StartEchoTest(targetCount, sendType, period, batchCount);
                break;
            }

            case "stop":
            {
                _manager?.StopEchoTest();
                break;
            }
            
            case "reconnect":
            {
                if (args.Length > 1 && int.TryParse(args[1], out var delayMs))
                {
                    _manager?.Test_Reconnect(delayMs);
                }
                else
                {
                    _manager?.Test_Reconnect(0);
                }
                break;
            }
            
            case "disconnect":
            {
                _manager?.Stop();
                break;
            }

            case "quit":
            {
                await _cts.CancelAsync();
                break;    
            }
            default:
            {
                Console.WriteLine($"Unknown command: {command}");
                break;
            }
        }
    }
}
