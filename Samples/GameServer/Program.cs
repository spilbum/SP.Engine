
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
        using var cts = new CancellationTokenSource();
        using var _ = SubscribeCtrlC(cts);

        try
        {
            var config = JsonConfigLoader.Load<AppConfig>("config.json", "config.dev.json");
            if (config == null)
                throw new InvalidOperationException("Failed to load config file(s).");

            using var server = new GameServer();
            if (!server.Initialize(config)) throw new InvalidOperationException("Failed to initialize server.");
            if (!server.Start()) throw new InvalidOperationException("Failed to start server.");

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
