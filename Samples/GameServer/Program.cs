
using System.Reflection;
using Common;
using SP.Core;
using SP.Engine.Server;

namespace GameServer;

internal static class Program
{
    private static IDisposable SubscribeCtrlC(CancellationTokenSource cts)
    {
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            if (!cts.IsCancellationRequested) cts.Cancel();
        };
        Console.CancelKeyPress += handler;
        return new Unsubscriber(() => Console.CancelKeyPress -= handler);
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private static async Task<int> Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = (Exception)e.ExceptionObject;
            Console.WriteLine($"[FATAL] UnhandledException: {ex.Message}");
            Console.WriteLine($"StackTrace: {ex.StackTrace}");
        };
        
        using var cts = new CancellationTokenSource();
        using var _ = SubscribeCtrlC(cts);

        GameServer? server = null;
        try
        {
            var appConfig = JsonConfigLoader.Load<AppConfig>("config.json", "config.dev.json");
            if (appConfig == null)
                throw new InvalidOperationException("Failed to load config file(s).");

            var builder = EngineBuilder<GameServer>.Create()
                .SetName(appConfig.Server.Name)
                .Listen(appConfig.Server.Port)
                .Listen(20000, mode: SocketMode.Udp)
                .AddAssembly(typeof(C2GProtocolData.LoginReq).Assembly)
                .ConfigureNetwork(network => network with
                {
                    SendingQueueSize = 128
                })
                .ConfigureSession(session => session with
                {
                    IdleSessionTimeoutSec = 60
                    #if DEBUG
                    , PeerJobSlowThresholdMs = 200
                    #endif
                })
                .ConfigurePerformance(perf => perf with
                {
                })
                .Setup(s =>
                {
                    if (!s.Setup(appConfig))
                        throw new InvalidOperationException("Failed to setup server.");
                });

            foreach (var connector in appConfig.Connector)
            {
                builder.AddConnector(connector.Name, connector.Host, connector.Port);   
            }

            server = builder.Build();
            if (!server.Start())
            {
                throw new InvalidOperationException("Failed to start server.");
            }
            
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Shutdown signal received");
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync($"Fatal: {e.Message}\n{e.StackTrace}");
            return 1;
        }
        finally
        {
            #if DEBUG
            BufferTracker.DumpLeaks(server?.Logger);
            #endif
            
            server?.Dispose();
        }
        
        return 0;
    }
}
