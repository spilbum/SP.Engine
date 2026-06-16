using EchoServer.Protocol;
using SP.Engine.Server;
using Exception = System.Exception;

namespace EchoServer;

public struct ServerConfig
{
    public string Name { get; set; }
    public int Port { get; set; }
}

public struct AppConfig
{
    public ServerConfig Server { get; set; }
}

internal static class Program
{
    private static readonly CancellationTokenSource CTS = new();
    
    private static async Task Main(string[] args)
    {
        var config = JsonConfigLoader.Load<AppConfig>("config.json");
        var builder = EngineBuilder<EchoServer>.Create()
            .SetName(config.Server.Name)
            .Listen(config.Server.Port)
            .Listen(20000, mode: SocketMode.Udp)
            .ConfigureNetwork(network => network with
            {

            })
            .ConfigureSession(session => session with
            {

            });

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
