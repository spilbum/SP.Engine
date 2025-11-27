
using Org.BouncyCastle.Tls;

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
        
        try
        {
            var config = JsonConfigLoader.Load<BuildConfig>("config.json", "config.dev.json");
            if (config == null)
                throw new InvalidOperationException("Failed to load config file(s).");

            using var server = new RankServer(cts.Token);
            if (!server.Initialize(config)) throw new InvalidOperationException("Failed to initialize.");
            if (!server.Start()) throw new InvalidOperationException("Failed to start.");

            await Task.Delay(Timeout.Infinite, cts.Token);
            
            server.Stop();
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync($"Fatal: {e.Message}\n{e.StackTrace}");
            return 1;
        }
    }
}
