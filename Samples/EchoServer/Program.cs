using SP.Engine.Server;
using Exception = System.Exception;

namespace EchoServer;

internal static class Program
{
    private static readonly CancellationTokenSource CTS = new();
    
    private static async Task Main(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var port))
        {
            Console.WriteLine("Usage: EchoServer.exe <port>");
            return;
        }
        
        var builder = EngineBuilder<EchoServer>.Create()
            .SetName(nameof(EchoServer))
            .Listen(port)
            .Listen(20000, mode: SocketMode.Udp);

        Console.CancelKeyPress += OnCancelKeyPress;

        EchoServer? server = null;
        try
        {
            server = builder.Build();
            if (!server.Start())
            {
                throw new Exception("Failed to start server");
            }

            Console.WriteLine("Server started. Press Ctrl+C to shut down.");

            await Task.Delay(Timeout.Infinite, CTS.Token);
        }
        catch (TaskCanceledException)
        {
            // 종료 시그널
        }
        catch (Exception ex)
        {
            Console.WriteLine("An exception occurred: {0}", ex.Message);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            server?.Stop();
            
            CTS.Dispose();
            Console.WriteLine("Server gracefully stopped.");
        }
    }
    
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Console.WriteLine("Shutdown signal received...");
        e.Cancel = true;

        if (!CTS.IsCancellationRequested)
        {
            CTS.Cancel();
        }
    }
}
