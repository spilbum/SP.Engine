
using Common;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;

namespace RankServer;

internal static class Program
{
    private static Unsubscriber SubscribeShutdown(CancellationTokenSource cts)
    {
        ConsoleCancelEventHandler handler = (sender, e) =>
        {
            e.Cancel = true;
            if (!cts.IsCancellationRequested) cts.Cancel();
        };

        Console.CancelKeyPress += handler;
        AppDomain.CurrentDomain.ProcessExit += Exit;
        
        return new Unsubscriber(() =>
        {
            Console.CancelKeyPress -= handler;
            AppDomain.CurrentDomain.ProcessExit -= Exit;
        });
        
        void Exit(object? o, EventArgs eventArgs)
        {
            if (!cts.IsCancellationRequested) cts.Cancel();
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
    
    private static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        using var _ = SubscribeShutdown(cts);
        
        RankServer? server = null;
        try
        {
            var appConfig = JsonConfigLoader.Load<AppConfig>("config.json", "config.dev.json");
            if (appConfig == null) throw new InvalidOperationException("Failed to load config file(s).");

            server = EngineBuilder<RankServer>.Create()
                .ConfigureNetwork(n => n with
                {
                })
                .ConfigureSession(s => s with
                {
                    MaxConnections = 100
                })
                .ConfigurePerformance(r => r with
                {
                    MonitorEnabled = true,
                    SamplePeriod = TimeSpan.FromSeconds(1),
                    LoggerEnabled = true,
                    LoggingPeriod = TimeSpan.FromSeconds(30)
                })
                .Setup(s =>
                {
                    if (!s.Setup(appConfig))
                        throw new InvalidOperationException("Failed to setup server.");
                })
                .Listen(appConfig.Server.Port)
                .AddAssembly(typeof(R2GProtocolData.RankMyAck).Assembly)
                .Build();
            
            if (!server.Start()) throw new InvalidOperationException("Failed to start server.");

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
            server?.Dispose();
        }
        
        return 0;
    }
}
